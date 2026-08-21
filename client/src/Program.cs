using System;
using System.Threading;
using System.Windows;

namespace MultimediaClient
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static void Main()
        {
            bool created;
            _mutex = new Mutex(true, "MultimediaClient_SingleInstance_9F3A", out created);
            if (!created) return; // 已有实例在运行

            Logger.Init();

            // 必须在最早启用 TLS1.2:配对窗口在此之前发 HTTPS 请求,
            // 裸编译的 exe 默认仅 TLS1.0,现代服务器(仅支持 1.2+)会直接拒绝握手
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls | System.Net.SecurityProtocolType.Tls11
                    | System.Net.SecurityProtocolType.Tls12;
                System.Net.ServicePointManager.DefaultConnectionLimit = 8;
            }
            catch { }

            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Logger.Error("未处理异常", e.ExceptionObject as Exception);
            };

            try
            {
                Config.Load();
                System.Windows.Forms.Application.EnableVisualStyles();

                Application app = new Application();
                app.DispatcherUnhandledException += delegate (object s,
                    System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    Logger.Error("界面异常(已拦截)", e.Exception);
                    e.Handled = true; // 拦截异常,保证看板程序不崩溃退出
                };
                app.SessionEnding += delegate (object s, SessionEndingCancelEventArgs e)
                {
                    Logger.Info("系统注销/关机");
                };

                if (!Config.IsPaired)
                {
                    PairingWindow pairing = new PairingWindow();
                    bool? ok = pairing.ShowDialog();
                    if (ok != true || !Config.IsPaired)
                    {
                        Logger.Info("未完成配对,退出");
                        return;
                    }
                }

                CacheService.Load();
                AppHost.Start(app);
                app.Run();
                AppHost.Shutdown();
            }
            catch (Exception ex)
            {
                Logger.Error("启动失败", ex);
                MessageBox.Show("程序启动失败:" + ex.Message, "多媒体任务看板",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
