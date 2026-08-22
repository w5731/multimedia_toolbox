using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace MultimediaClient
{
    /// <summary>系统托盘:版本信息 / 设置 / 立即刷新 / 退出(需密码)</summary>
    internal class TrayService : IDisposable
    {
        private NotifyIcon _tray;
        private ToolStripMenuItem _verLatestItem;
        private bool _online;

        public void Start()
        {
            _tray = new NotifyIcon();
            _tray.Icon = CreateIcon();
            _tray.Text = "多媒体任务看板";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            // 版本信息(只读展示):当前版本固定,服务器最新随心跳更新,打开菜单时刷新
            ToolStripMenuItem verCurrent = new ToolStripMenuItem("当前版本  v" + AppHost.Version);
            verCurrent.Enabled = false;
            _verLatestItem = new ToolStripMenuItem("服务器最新  获取中…");
            _verLatestItem.Enabled = false;
            menu.Items.Add(verCurrent);
            menu.Items.Add(_verLatestItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("设置", null, delegate { OpenSettings(); });
            menu.Items.Add("立即刷新", null, delegate { AppHost.RefreshNow(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { TryExit(); });
            menu.Opening += delegate { RefreshVersionItem(); };
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { OpenSettings(); };
        }

        private void RefreshVersionItem()
        {
            try
            {
                string latest = DataStore.LatestVersion;
                if (latest.Length == 0)
                {
                    _verLatestItem.Text = "服务器最新  " + (_online ? "未发布" : "未知(离线)");
                }
                else
                {
                    bool newer = SelfUpdate.ShouldUpdate(latest);
                    _verLatestItem.Text = "服务器最新  v" + latest + (newer ? "(有新版)" : "(已是最新)");
                }
            }
            catch { }
        }

        public void SetStatus(bool online, string className)
        {
            _online = online;
            try
            {
                if (_tray != null)
                {
                    _tray.Text = "多媒体任务看板 - " + (className.Length > 0 ? className : "未配对")
                        + (online ? "(在线)" : "(离线)");
                }
            }
            catch { }
        }

        private void OpenSettings()
        {
            PasswordWindow pwd = new PasswordWindow("设置验证");
            if (pwd.ShowDialog() == true && pwd.Passed)
            {
                SettingsWindow sw = new SettingsWindow();
                sw.ShowDialog();
            }
        }

        private void TryExit()
        {
            PasswordWindow pwd = new PasswordWindow("退出验证");
            if (pwd.ShowDialog() == true && pwd.Passed)
            {
                AppHost.Shutdown();
            }
        }

        private static Icon CreateIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(22, 33, 62)))
                {
                    g.FillEllipse(bg, 1, 1, 30, 30);
                }
                using (System.Drawing.Font f = new System.Drawing.Font("Microsoft YaHei", 15, System.Drawing.FontStyle.Bold))
                using (SolidBrush fg = new SolidBrush(Color.White))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("看", f, fg, new RectangleF(0, 0, 32, 32), sf);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }
    }
}
