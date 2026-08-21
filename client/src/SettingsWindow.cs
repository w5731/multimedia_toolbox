using System;
using System.Windows;
using System.Windows.Controls;

namespace MultimediaClient
{
    /// <summary>
    /// 本地设置窗口(需密码进入)。
    /// 弹窗时长/音量/看板位置/字号由教师端远程下发,此处只读展示。
    /// </summary>
    internal class SettingsWindow : Window
    {
        private readonly TextBox _serverInput;
        private readonly CheckBox _autoStartBox;
        private readonly ComboBox _modeBox;
        private readonly TextBox _pwdOld;
        private readonly TextBox _pwdNew;
        private readonly TextBlock _info;
        private readonly TextBlock _error;

        public SettingsWindow()
        {
            Title = "客户端设置";
            Width = 440; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = UiKit.Font; FontSize = 14;
            Topmost = true;

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(22, 14, 22, 18);
            scroll.Content = panel;

            _info = new TextBlock();
            _info.FontSize = 13;
            _info.Foreground = UiKit.Gray;
            _info.TextWrapping = TextWrapping.Wrap;
            _info.Text = "班级:" + Config.ClassName
                + "\n弹窗时长 " + DataStore.Settings.PopupSeconds + " 秒"
                + " · 静音恢复音量 " + DataStore.Settings.Volume + "%"
                + " · 位置 " + PositionName(DataStore.Settings.OverlayPosition)
                + " · 字号 x" + DataStore.Settings.FontScale
                + "\n(以上由教师端远程控制)";
            panel.Children.Add(_info);

            panel.Children.Add(UiKit.Label("服务器地址"));
            _serverInput = UiKit.Input(Config.ServerUrl);
            panel.Children.Add(_serverInput);

            panel.Children.Add(UiKit.Label("看板显示模式"));
            _modeBox = new ComboBox();
            _modeBox.FontSize = 15;
            _modeBox.Padding = new Thickness(8, 6, 8, 6);
            _modeBox.Items.Add("标准(悬浮于壁纸之上,兼容性最好)");
            _modeBox.Items.Add("嵌入壁纸层(图标之下)");
            _modeBox.SelectedIndex = Config.OverlayMode == "wallpaper" ? 1 : 0;
            panel.Children.Add(_modeBox);

            _autoStartBox = new CheckBox();
            _autoStartBox.Content = "开机自动启动";
            _autoStartBox.FontSize = 14;
            _autoStartBox.Margin = new Thickness(0, 14, 0, 0);
            _autoStartBox.IsChecked = Config.AutoStart;
            panel.Children.Add(_autoStartBox);

            panel.Children.Add(UiKit.Label("修改管理密码(原密码)"));
            _pwdOld = UiKit.Input("");
            panel.Children.Add(_pwdOld);
            panel.Children.Add(UiKit.Label("新密码(留空则不修改)"));
            _pwdNew = UiKit.Input("");
            panel.Children.Add(_pwdNew);

            _error = UiKit.ErrorText();
            panel.Children.Add(_error);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            Button cancel = UiKit.PrimaryButton("取消");
            cancel.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(120, 144, 156));
            Button save = UiKit.PrimaryButton("保存");
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            Content = scroll;

            cancel.Click += delegate { Close(); };
            save.Click += delegate { Save(); };
        }

        private static string PositionName(string pos)
        {
            if (pos == "left") return "屏幕左侧";
            if (pos == "top") return "屏幕顶部";
            return "屏幕右侧";
        }

        private void Save()
        {
            _error.Text = "";
            string server = _serverInput.Text.Trim().TrimEnd('/');
            if (!server.StartsWith("http://") && !server.StartsWith("https://"))
            {
                _error.Text = "服务器地址应以 http:// 或 https:// 开头";
                return;
            }
            if (_pwdNew.Text.Length > 0)
            {
                if (_pwdOld.Text != Config.ExitPassword)
                {
                    _error.Text = "原密码错误";
                    return;
                }
                if (_pwdNew.Text.Length < 4)
                {
                    _error.Text = "新密码至少 4 位";
                    return;
                }
                Config.ExitPassword = _pwdNew.Text;
            }

            bool modeChanged = (_modeBox.SelectedIndex == 1) != (Config.OverlayMode == "wallpaper");
            Config.ServerUrl = server;
            Config.OverlayMode = _modeBox.SelectedIndex == 1 ? "wallpaper" : "normal";
            Config.AutoStart = _autoStartBox.IsChecked == true;
            Config.Save();
            AutoStart.Set(Config.AutoStart);

            AppHost.ReloadApiBase();

            if (modeChanged)
            {
                MessageBox.Show("看板显示模式将在下次启动时生效。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Close();
        }
    }
}
