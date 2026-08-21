using System;
using System.Windows;
using System.Windows.Controls;

namespace MultimediaClient
{
    /// <summary>退出前密码确认,防止学生误关程序</summary>
    internal class PasswordWindow : Window
    {
        private readonly PasswordBox _pwd;
        private readonly TextBlock _error;
        public bool Passed { get; private set; }

        public PasswordWindow(string title)
        {
            Title = title;
            Width = 340; Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = UiKit.Font; FontSize = 14;
            Topmost = true;

            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(20, 14, 20, 14);
            panel.Children.Add(UiKit.Label("请输入管理密码:"));
            _pwd = new PasswordBox();
            _pwd.FontSize = 15;
            _pwd.Padding = new Thickness(8, 6, 8, 6);
            panel.Children.Add(_pwd);
            _error = UiKit.ErrorText();
            panel.Children.Add(_error);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            Button cancel = UiKit.PrimaryButton("取消");
            cancel.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(120, 144, 156));
            Button ok = UiKit.PrimaryButton("确定");
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);
            Content = panel;

            ok.Click += delegate { Check(); };
            cancel.Click += delegate { DialogResult = false; Close(); };
            _pwd.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Enter) Check();
            };
            Loaded += delegate { _pwd.Focus(); };
        }

        private void Check()
        {
            if (_pwd.Password == Config.ExitPassword)
            {
                Passed = true;
                DialogResult = true;
                Close();
            }
            else
            {
                _error.Text = "密码错误";
                _pwd.SelectAll();
                _pwd.Focus();
            }
        }
    }
}
