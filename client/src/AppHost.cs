using System;
using System.Net;
using System.Threading;
using System.Windows;

namespace MultimediaClient
{
    /// <summary>
    /// 应用主控:持有看板窗口、托盘、轮询服务,串起所有事件。
    /// </summary>
    internal static class AppHost
    {
        public const string Version = "1.1.0";

        private static ApiClient _api;
        private static PollService _poll;
        private static OverlayWindow _overlay;
        private static TrayService _tray;
        private static CallPopupWindow _popup;
        private static Application _app;
        private static bool _stopping;

        public static void Start(Application app)
        {
            _app = app;

            // TLS 设置已在 Program.Main 最早处完成(配对窗口也需要)

            if (Config.AutoStart) AutoStart.Set(true);

            _api = new ApiClient();
            _api.BaseUrl = Config.ServerUrl;

            _overlay = new OverlayWindow();
            _overlay.ApplySettings();
            _overlay.Show();

            _tray = new TrayService();
            _tray.Start();

            _poll = new PollService(_api, app.Dispatcher);
            _poll.CallReceived += OnCallReceived;
            _poll.OnlineChanged += OnOnlineChanged;
            _poll.AuthInvalid += OnAuthInvalid;
            _poll.UpdateAvailable += OnUpdateAvailable;
            DataStore.Updated += delegate { _overlay.ApplySettings(); };
            _poll.Start();

            Logger.Info("客户端已启动 v" + Version + " 班级:" + Config.ClassName);
        }

        public static void RefreshNow()
        {
            // 强制认为数据过期,下一次心跳将重新拉取
            DataStore.DataVersion = -1;
            if (_poll != null) _poll.Kick();
        }

        public static void ReloadApiBase()
        {
            if (_api != null) _api.BaseUrl = Config.ServerUrl;
            RefreshNow();
        }

        public static void Shutdown()
        {
            _stopping = true;
            Logger.Info("客户端退出");
            if (_poll != null) _poll.Stop();
            if (_tray != null) _tray.Dispose();
            if (_app != null) _app.Shutdown();
        }

        private static void OnCallReceived(CallInfo call)
        {
            if (_stopping) return;
            // 已有弹窗时先关掉旧的,直接展示最新叫号
            if (_popup != null)
            {
                try { _popup.Close(); } catch { }
                _popup = null;
            }
            CallPopupWindow popup = new CallPopupWindow(call, DataStore.Settings);
            _popup = popup;
            popup.PopupClosed += delegate(CallInfo c)
            {
                _popup = null;
                _poll.AckCall(c.Id, "closed");
            };
            popup.ShowPopup();
            _poll.AckCall(call.Id, "shown");
            Logger.Info("收到叫号 #" + call.Id + " " + call.Numbers + " → " + call.Destination);
        }

        private static void OnOnlineChanged(bool online)
        {
            if (_overlay != null) _overlay.SetOffline(!online);
            if (_tray != null) _tray.SetStatus(online, Config.ClassName);
        }

        // 服务器发布了新版本:后台下载完成后重启完成热替换
        private static void OnUpdateAvailable(string version)
        {
            if (_stopping) return;
            // 叫号弹窗期间不打断学生,3 秒后的心跳会再次通知,届时再更新
            if (_popup != null) return;
            SelfUpdate.Begin(_api, version, _app.Dispatcher, ApplyUpdate);
        }

        private static void ApplyUpdate(string batPath)
        {
            if (_stopping) return;
            SelfUpdate.RestartWith(batPath);
        }

        private static void OnAuthInvalid()
        {
            MessageBox.Show("本机已在教师端被解绑,请重新配对。", "多媒体任务看板",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Config.ClientId = 0;
            Config.Token = "";
            Config.Save();
            PairingWindow p = new PairingWindow();
            if (p.ShowDialog() == true && Config.IsPaired)
            {
                ReloadApiBase();
            }
        }
    }
}
