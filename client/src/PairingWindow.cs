using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace MultimediaClient
{
    /// <summary>首次运行配对向导:输入服务器地址与教师端生成的配对码</summary>
    internal class PairingWindow : Window
    {
        private readonly TextBox _serverInput;
        private readonly TextBox _codeInput;
        private readonly TextBox _nameInput;
        private readonly TextBlock _error;
        private readonly Button _ok;

        public PairingWindow()
        {
            Window w = UiKit.MakeDialog("客户端配对 - 多媒体任务看板", 420, 380);
            // 把样式复制到自身(WPF 窗口内容只能在自身构建)
            Title = w.Title; Width = w.Width; Height = w.Height;
            WindowStartupLocation = w.WindowStartupLocation;
            ResizeMode = w.ResizeMode; FontFamily = UiKit.Font; FontSize = 14;
            Background = w.Background;

            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(24, 16, 24, 16);

            TextBlock title = new TextBlock();
            title.Text = "首次使用,请与教师端配对";
            title.FontSize = 19;
            title.FontWeight = FontWeights.Bold;
            title.Foreground = UiKit.Dark;
            panel.Children.Add(title);

            TextBlock tip = new TextBlock();
            tip.Text = "在教师网站的「客户端」页生成配对码后,在下方输入。";
            tip.Foreground = UiKit.Gray;
            tip.TextWrapping = TextWrapping.Wrap;
            tip.Margin = new Thickness(0, 6, 0, 4);
            panel.Children.Add(tip);

            panel.Children.Add(UiKit.Label("服务器地址"));
            _serverInput = UiKit.Input(Config.ServerUrl.Length > 0 ? Config.ServerUrl : "http://");
            panel.Children.Add(_serverInput);

            panel.Children.Add(UiKit.Label("配对码(6 位数字)"));
            _codeInput = UiKit.Input("");
            panel.Children.Add(_codeInput);

            panel.Children.Add(UiKit.Label("这台电脑的名称(方便教师识别)"));
            _nameInput = UiKit.Input(Environment.MachineName);
            panel.Children.Add(_nameInput);

            _error = UiKit.ErrorText();
            panel.Children.Add(_error);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            _ok = UiKit.PrimaryButton("配 对");
            buttons.Children.Add(_ok);
            panel.Children.Add(buttons);

            Content = panel;

            _ok.Click += delegate { DoPair(); };
        }

        private void DoPair()
        {
            string server = _serverInput.Text.Trim().TrimEnd('/');
            string code = _codeInput.Text.Trim();
            string name = _nameInput.Text.Trim();
            _error.Text = "";
            if (!server.StartsWith("http://") && !server.StartsWith("https://"))
            {
                _error.Text = "服务器地址应以 http:// 或 https:// 开头";
                return;
            }
            if (code.Length == 0)
            {
                _error.Text = "请输入配对码";
                return;
            }
            _ok.IsEnabled = false;
            _ok.Content = "配对中...";

            ApiClient api = new ApiClient();
            api.BaseUrl = server;
            api.TimeoutMs = 8000;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["code"] = code;
            body["machine_code"] = Config.MachineCode;
            body["name"] = name;

            ThreadPool.QueueUserWorkItem(delegate
            {
                ApiResult r = api.Post("/api/client/pair", body);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _ok.IsEnabled = true;
                    _ok.Content = "配 对";
                    if (r.Ok)
                    {
                        Config.ServerUrl = server;
                        Config.ClientId = Json.GetInt(r.Data, "client_id", 0);
                        Config.Token = Json.GetString(r.Data, "token", "");
                        Config.ClassName = Json.GetString(r.Data, "class_name", "");
                        TimeSync.Update(Json.GetString(r.Data, "server_time", ""));
                        Config.Save();
                        MessageBox.Show("配对成功!班级:" + Config.ClassName, "多媒体任务看板",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        DialogResult = true;
                        Close();
                    }
                    else
                    {
                        _error.Text = r.Error.Length > 0 ? r.Error : "配对失败,请检查服务器地址与配对码";
                    }
                }));
            });
        }
    }
}
