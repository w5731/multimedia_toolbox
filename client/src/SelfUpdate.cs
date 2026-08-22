using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace MultimediaClient
{
    /// <summary>
    /// 自动更新:心跳返回的 latest_version 高于当前版本时,
    /// 后台下载新 exe 并校验,然后启动一个批处理在本进程退出后替换程序并重启。
    /// 防循环:已应用过的版本号写入配置,即使服务器发错包也不会反复更新同一版本。
    /// </summary>
    internal static class SelfUpdate
    {
        public static bool InProgress;
        private static DateTime _lastFail = DateTime.MinValue;

        /// <summary>是否需要更新:remote 高于当前运行版本,且不是已经应用过的版本</summary>
        public static bool ShouldUpdate(string remote)
        {
            if (remote == null || remote.Length == 0) return false;
            if (remote == Config.LastUpdateVersion) return false;
            return CompareVersions(remote, AppHost.Version) > 0;
        }

        /// <summary>三段数字版本号比较:a 高返回 1,相等 0,a 低返回 -1;非法段按 0 处理</summary>
        public static int CompareVersions(string a, string b)
        {
            int[] pa = Parse(a), pb = Parse(b);
            for (int i = 0; i < 3; i++)
            {
                if (pa[i] != pb[i]) return pa[i] > pb[i] ? 1 : -1;
            }
            return 0;
        }

        private static int[] Parse(string v)
        {
            int[] r = new int[3];
            string[] parts = (v ?? "").Split('.');
            for (int i = 0; i < 3; i++)
            {
                int n;
                r[i] = (i < parts.Length && int.TryParse(parts[i], out n) && n >= 0) ? n : 0;
            }
            return r;
        }

        /// <summary>后台开始下载;成功后通过 UI 线程回调 onReady(参数为替换批处理路径)</summary>
        public static void Begin(ApiClient api, string version, Dispatcher ui, Action<string> onReady)
        {
            if (InProgress) return;
            if ((DateTime.Now - _lastFail).TotalMinutes < 30) return; // 失败冷却,避免反复下载
            InProgress = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string bat = null;
                try
                {
                    bat = Download(api, version);
                }
                catch (Exception ex)
                {
                    Logger.Error("自动更新失败(v" + version + ")", ex);
                }
                if (bat != null)
                {
                    ui.BeginInvoke(onReady, bat);
                }
                else
                {
                    _lastFail = DateTime.Now;
                    InProgress = false;
                }
            });
        }

        private static string Download(ApiClient api, string version)
        {
            Logger.Info("发现新版本 v" + version + "(当前 v" + AppHost.Version + "),开始自动更新");

            // 1) 读取服务器公布的校验信息
            Dictionary<string, string> q = new Dictionary<string, string>();
            q["client_id"] = Config.ClientId.ToString();
            q["token"] = Config.Token;
            int expectSize = -1;
            string expectSha = "";
            ApiResult info = api.Get("/api/client/update-info", q);
            if (info.Ok)
            {
                Dictionary<string, object> u = Json.GetObject(info.Data, "update");
                if (u != null)
                {
                    expectSize = Json.GetInt(u, "size", -1);
                    expectSha = Json.GetString(u, "sha256", "");
                }
            }

            // 2) 流式下载到 update 目录
            string dir = Path.Combine(Config.AppDir, "update");
            Directory.CreateDirectory(dir);
            string tmp = Path.Combine(dir, "MultimediaClient.new.exe");
            string url = api.BaseUrl.TrimEnd('/') + "/api/client/update-download?client_id="
                + Config.ClientId + "&token=" + Uri.EscapeDataString(Config.Token);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.Proxy = null;
            req.UserAgent = "MultimediaClient/" + AppHost.Version;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream rs = resp.GetResponseStream())
            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[65536];
                int n;
                while ((n = rs.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }

            // 3) 完整性校验:PE 头、大小、sha256(服务器未提供校验值时跳过对应项)
            long size = new FileInfo(tmp).Length;
            if (size < 1024) throw new Exception("下载的安装包过小,已放弃更新");
            using (FileStream fs = new FileStream(tmp, FileMode.Open, FileAccess.Read))
            {
                if (fs.ReadByte() != 0x4D || fs.ReadByte() != 0x5A)
                    throw new Exception("安装包不是有效的可执行程序");
            }
            if (expectSize > 0 && size != expectSize) throw new Exception("安装包大小校验失败");
            if (expectSha.Length == 64)
            {
                string actual;
                using (FileStream fs = new FileStream(tmp, FileMode.Open, FileAccess.Read))
                using (SHA256 sha = SHA256.Create())
                {
                    actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                }
                if (!String.Equals(actual, expectSha, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("安装包哈希校验失败");
            }

            // 4) 生成替换批处理;记录已应用版本号防止更新循环
            string exe = Assembly.GetExecutingAssembly().Location;
            string bat = Path.Combine(dir, "apply-update.bat");
            WriteBat(bat, Process.GetCurrentProcess().Id, exe, tmp);
            Config.LastUpdateVersion = version;
            Config.Save();
            Logger.Info("更新包已就绪(v" + version + ", " + (size / 1024) + "KB),即将重启完成升级");
            return bat;
        }

        // 批处理在进程退出后执行:备份旧 exe → 替换 → 重启 → 自删除
        // 注意必须用系统 ANSI 编码写出,cmd.exe 才能正确解析含中文的路径
        private static void WriteBat(string batPath, int pid, string targetExe, string newExe)
        {
            string[] lines = new string[]
            {
                "@echo off",
                "set PID=" + pid,
                "set TARGET=" + targetExe,
                "set NEW=" + newExe,
                "set N=0",
                ":wait",
                "tasklist /FI \"PID eq %PID%\" /NH | find \"%PID%\" >nul",
                "if errorlevel 1 goto swap",
                "set /a N+=1",
                "if %N% GEQ 60 goto swap",
                "ping 127.0.0.1 -n 2 >nul",
                "goto wait",
                ":swap",
                "copy /y \"%TARGET%\" \"%TARGET%.bak\" >nul",
                "move /y \"%NEW%\" \"%TARGET%\" >nul",
                "start \"\" \"%TARGET%\"",
                "del \"%~f0\"",
            };
            File.WriteAllLines(batPath, lines, Encoding.Default);
        }

        /// <summary>启动替换批处理并退出当前程序(批处理会重启新 exe)</summary>
        public static void RestartWith(string batPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"" + batPath + "\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch (Exception ex)
            {
                Logger.Error("启动更新程序失败", ex);
                InProgress = false;
                return;
            }
            AppHost.Shutdown();
        }
    }
}
