using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MultimediaClient
{
    /// <summary>本地配置(服务器地址、凭据、退出密码等),保存在程序目录 config.json</summary>
    internal static class Config
    {
        public static string ServerUrl = "";
        public static int ClientId = 0;
        public static string Token = "";
        public static string ClassName = "";
        public static string MachineCode = "";
        public static string MachineName = "";
        public static string ExitPassword = "123456";
        public static string OverlayMode = "normal"; // normal | wallpaper
        public static bool AutoStart = true;
        // 最近一次已成功下载并应用的更新版本号,防止服务器发错包导致反复更新
        public static string LastUpdateVersion = "";

        public static bool IsPaired
        {
            get { return ClientId > 0 && Token.Length > 0 && ServerUrl.Length > 0; }
        }

        private static string _dir;

        public static string AppDir
        {
            get
            {
                if (_dir == null)
                {
                    _dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    // 目录不可写时(例如 Program Files)回退到 LocalAppData
                    try
                    {
                        string probe = Path.Combine(_dir, ".write_test");
                        File.WriteAllText(probe, "1");
                        File.Delete(probe);
                    }
                    catch
                    {
                        _dir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "MultimediaClient");
                        Directory.CreateDirectory(_dir);
                    }
                }
                return _dir;
            }
        }

        public static string ConfigPath { get { return Path.Combine(AppDir, "config.json"); } }

        public static void Load()
        {
            MachineName = Environment.MachineName;
            MachineCode = BuildMachineCode();
            try
            {
                if (!File.Exists(ConfigPath)) return;
                Dictionary<string, object> d = Json.ParseObject(File.ReadAllText(ConfigPath, Encoding.UTF8));
                if (d == null) return;
                ServerUrl = Json.GetString(d, "server_url", "");
                ClientId = Json.GetInt(d, "client_id", 0);
                Token = Json.GetString(d, "token", "");
                ClassName = Json.GetString(d, "class_name", "");
                ExitPassword = Json.GetString(d, "exit_password", "123456");
                OverlayMode = Json.GetString(d, "overlay_mode", "normal");
                AutoStart = Json.GetBool(d, "auto_start", true);
                LastUpdateVersion = Json.GetString(d, "last_update_version", "");
            }
            catch (Exception ex)
            {
                Logger.Error("读取配置失败,使用默认配置", ex);
            }
        }

        public static void Save()
        {
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["server_url"] = ServerUrl;
                d["client_id"] = ClientId;
                d["token"] = Token;
                d["class_name"] = ClassName;
                d["exit_password"] = ExitPassword;
                d["overlay_mode"] = OverlayMode;
                d["auto_start"] = AutoStart;
                d["last_update_version"] = LastUpdateVersion;
                AtomicWrite(ConfigPath, Json.Serialize(d));
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败", ex);
            }
        }

        /// <summary>原子写文件:先写临时文件再替换,避免断电/崩溃导致配置损坏</summary>
        internal static void AtomicWrite(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Encoding.UTF8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>稳定的机器标识:机器名 + 用户名 的短哈希</summary>
        private static string BuildMachineCode()
        {
            try
            {
                string raw = Environment.MachineName + "|" + Environment.UserName;
                using (MD5 md5 = MD5.Create())
                {
                    byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2"));
                    return Environment.MachineName + "-" + sb.ToString();
                }
            }
            catch
            {
                return Environment.MachineName;
            }
        }
    }
}
