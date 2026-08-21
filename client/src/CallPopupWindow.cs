using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MultimediaClient
{
    /// <summary>
    /// 叫号置顶弹窗:超大字号、响铃、倒计时自动关闭。
    /// 若系统静音,自动取消静音并把音量调整到设定值(默认 50%)。
    /// 号数使用 Viewbox 自适应缩放,任意长度都不会溢出。
    /// </summary>
    internal class CallPopupWindow : Window
    {
        private static readonly FontFamily Font = new FontFamily("Microsoft YaHei");
        private static readonly SolidColorBrush Red = new SolidColorBrush(Color.FromRgb(211, 47, 47));

        private readonly CallInfo _call;
        private readonly int _seconds;
        private readonly DispatcherTimer _timer;
        private int _remainMs;
        private Border _progressFill;
        private double _progressMaxWidth;
        private TextBlock _countText;

        public event Action<CallInfo> PopupClosed;

        public CallPopupWindow(CallInfo call, Settings settings)
        {
            _call = call;
            _seconds = Math.Max(3, settings.PopupSeconds);
            _remainMs = _seconds * 1000;

            double scale = settings.FontScale;
            Width = 720 * scale;
            Height = 500 * scale;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            Background = Brushes.Transparent;
            AllowsTransparency = true;

            Border root = new Border();
            root.Background = Brushes.White;
            root.BorderBrush = Red;
            root.BorderThickness = new Thickness(6);
            root.CornerRadius = new CornerRadius(20);
            root.Padding = new Thickness(28, 20, 28, 20);

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // 标题
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 号数
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // 地点
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // 原因
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // 进度条
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // 按钮
            root.Child = grid;

            TextBlock header = MakeText("叫 号 通 知", 32 * scale, Red, FontWeights.Bold);
            header.TextAlignment = TextAlignment.Center;
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // 号数:Viewbox 自适应缩放,保证任何长度都完整显示
            Viewbox numbersBox = new Viewbox();
            numbersBox.Stretch = Stretch.Uniform;
            numbersBox.StretchDirection = StretchDirection.DownOnly;
            numbersBox.Margin = new Thickness(0, 4 * scale, 0, 4 * scale);
            TextBlock numbers = MakeText(call.Numbers, 96 * scale, Red, FontWeights.Bold);
            numbers.TextWrapping = TextWrapping.NoWrap;
            numbersBox.Child = numbers;
            Grid.SetRow(numbersBox, 1);
            grid.Children.Add(numbersBox);

            TextBlock dest = MakeText("请前往  " + call.Destination, 36 * scale,
                new SolidColorBrush(Color.FromRgb(38, 50, 56)), FontWeights.Bold);
            dest.TextAlignment = TextAlignment.Center;
            Grid.SetRow(dest, 2);
            grid.Children.Add(dest);

            if (call.Reason.Length > 0)
            {
                TextBlock reason = MakeText("(" + call.Reason + ")", 24 * scale,
                    new SolidColorBrush(Color.FromRgb(90, 100, 110)), FontWeights.Normal);
                reason.TextAlignment = TextAlignment.Center;
                reason.Margin = new Thickness(0, 4 * scale, 0, 0);
                Grid.SetRow(reason, 3);
                grid.Children.Add(reason);
            }

            // 倒计时进度条
            StackPanel bottom = new StackPanel();
            Border track = new Border();
            track.Background = new SolidColorBrush(Color.FromRgb(238, 238, 238));
            track.CornerRadius = new CornerRadius(5);
            track.Height = 10;
            track.Margin = new Thickness(0, 10 * scale, 0, 4 * scale);
            _progressFill = new Border();
            _progressFill.Background = Red;
            _progressFill.CornerRadius = new CornerRadius(5);
            _progressFill.HorizontalAlignment = HorizontalAlignment.Left;
            track.Child = _progressFill;
            bottom.Children.Add(track);

            _countText = MakeText("", 16 * scale,
                new SolidColorBrush(Color.FromRgb(120, 120, 120)), FontWeights.Normal);
            _countText.TextAlignment = TextAlignment.Center;
            bottom.Children.Add(_countText);
            Grid.SetRow(bottom, 4);
            grid.Children.Add(bottom);

            Button close = new Button();
            close.Content = "关  闭";
            close.FontFamily = Font;
            close.FontSize = 26 * scale;
            close.FontWeight = FontWeights.Bold;
            close.Foreground = Brushes.White;
            close.Background = Red;
            close.BorderThickness = new Thickness(0);
            close.Padding = new Thickness(56, 8, 56, 8);
            close.Margin = new Thickness(0, 10 * scale, 0, 0);
            close.HorizontalAlignment = HorizontalAlignment.Center;
            close.Cursor = System.Windows.Input.Cursors.Hand;
            close.Click += delegate { Close(); };
            Grid.SetRow(close, 5);
            grid.Children.Add(close);

            Content = root;

            // 边框红色呼吸闪烁,吸引注意
            ColorAnimation flash = new ColorAnimation();
            flash.From = Color.FromRgb(211, 47, 47);
            flash.To = Color.FromRgb(255, 138, 101);
            flash.Duration = TimeSpan.FromMilliseconds(700);
            flash.AutoReverse = true;
            flash.RepeatBehavior = RepeatBehavior.Forever;
            root.BorderBrush = new SolidColorBrush(Colors.Transparent);
            root.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, flash);

            Loaded += delegate
            {
                _progressMaxWidth = track.ActualWidth;
                AudioService.EnsureAudible(settings.Volume);
                AudioService.StartBell();
            };

            base.Closed += delegate
            {
                _timer.Stop();
                AudioService.StopBell();
                Action<CallInfo> a = PopupClosed;
                if (a != null) a(_call);
            };

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += delegate
            {
                _remainMs -= 100;
                if (_remainMs <= 0) { Close(); return; }
                double frac = (double)_remainMs / (_seconds * 1000);
                _progressFill.Width = _progressMaxWidth * frac;
                _countText.Text = Math.Ceiling(_remainMs / 1000.0) + " 秒后自动关闭";
            };
        }

        public void ShowPopup()
        {
            Show();
            _timer.Start();
        }

        private static TextBlock MakeText(string text, double size, Brush brush, FontWeight weight)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = Font;
            t.FontSize = size;
            t.Foreground = brush;
            t.FontWeight = weight;
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }
    }
}
