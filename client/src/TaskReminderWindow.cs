using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MultimediaClient
{
    /// <summary>任务结束全屏提醒:大字、置顶、密码提前退出、五分钟自动关闭。</summary>
    internal sealed class TaskReminderWindow : Window
    {
        private const int AutoCloseSeconds = 5 * 60;
        private static readonly FontFamily Font = new FontFamily("Microsoft YaHei");
        private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(28, 25, 23));
        private static readonly Brush WhiteBrush = new SolidColorBrush(Color.FromRgb(250, 250, 249));
        private static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(214, 211, 209));
        private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        private static readonly Brush AmberDarkBrush = new SolidColorBrush(Color.FromRgb(180, 83, 9));

        private readonly TaskItem _task;
        private readonly DispatcherTimer _timer;
        private readonly TextBlock _countdown;
        private int _remainingSeconds = AutoCloseSeconds;
        private bool _allowClose;
        private bool _closedRaised;
        private bool _passwordDialogOpen;

        public event Action ReminderClosed;

        public TaskReminderWindow(TaskItem task)
        {
            _task = task;
            Title = "任务提醒";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Topmost = true;
            ShowInTaskbar = false;
            Background = BackgroundBrush;
            FontFamily = Font;

            Rect screen = new Rect(0, 0, Width, Height);
            double scale = Math.Max(0.9, Math.Min(1.45, Math.Min(screen.Width / 1920.0, screen.Height / 1080.0)));

            Grid root = new Grid();
            root.Margin = new Thickness(64 * scale, 42 * scale, 64 * scale, 36 * scale);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock header = MakeText("任 务 提 醒", 34 * scale, AmberBrush, FontWeights.Bold);
            header.TextAlignment = TextAlignment.Center;
            header.Margin = new Thickness(0, 0, 0, 20 * scale);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.Focusable = false;
            Grid.SetRow(scroller, 1);

            StackPanel content = new StackPanel();
            content.VerticalAlignment = VerticalAlignment.Center;
            content.MaxWidth = 1500;
            content.Margin = new Thickness(20 * scale, 0, 20 * scale, 0);

            Border status = new Border();
            status.Background = new SolidColorBrush(Color.FromRgb(69, 55, 25));
            status.CornerRadius = new CornerRadius(999);
            status.Padding = new Thickness(24 * scale, 8 * scale, 24 * scale, 8 * scale);
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.Margin = new Thickness(0, 0, 0, 22 * scale);
            TextBlock statusText = MakeText(task.IsRange ? "时间段已结束" : "任务时间已到",
                27 * scale, AmberBrush, FontWeights.Bold);
            status.Child = statusText;
            content.Children.Add(status);

            TextBlock title = MakeText(task.Title, 74 * scale, WhiteBrush, FontWeights.Bold);
            title.TextAlignment = TextAlignment.Center;
            title.LineHeight = 96 * scale;
            title.Margin = new Thickness(0, 0, 0, 28 * scale);
            content.Children.Add(title);

            DateTime now = TimeSync.Now;
            string[] weekNames = { "日", "一", "二", "三", "四", "五", "六" };
            string infoText = task.TimeText + "  ·  " + now.ToString("yyyy年M月d日")
                + "  星期" + weekNames[(int)now.DayOfWeek];
            if (Config.ClassName.Length > 0) infoText += "  ·  " + Config.ClassName;
            TextBlock info = MakeText(infoText, 30 * scale, DimBrush, FontWeights.SemiBold);
            info.TextAlignment = TextAlignment.Center;
            info.Margin = new Thickness(0, 0, 0, 18 * scale);
            content.Children.Add(info);

            if (task.Remark.Length > 0)
            {
                Border remarkCard = MakeCard(scale);
                TextBlock remark = MakeText("任务信息  " + task.Remark, 30 * scale, DimBrush, FontWeights.Normal);
                remark.TextAlignment = TextAlignment.Center;
                remarkCard.Child = remark;
                content.Children.Add(remarkCard);
            }

            if (task.ReminderText.Length > 0)
            {
                Border promptCard = MakeCard(scale);
                promptCard.Background = new SolidColorBrush(Color.FromRgb(255, 247, 237));
                promptCard.BorderBrush = AmberBrush;
                promptCard.BorderThickness = new Thickness(2);
                TextBlock prompt = MakeText(task.ReminderText, 42 * scale, AmberDarkBrush, FontWeights.Bold);
                prompt.TextAlignment = TextAlignment.Center;
                prompt.LineHeight = 58 * scale;
                promptCard.Child = prompt;
                content.Children.Add(promptCard);
            }

            scroller.Content = content;
            root.Children.Add(scroller);

            StackPanel footer = new StackPanel();
            footer.Margin = new Thickness(0, 22 * scale, 0, 0);
            _countdown = MakeText("", 22 * scale, DimBrush, FontWeights.Normal);
            _countdown.TextAlignment = TextAlignment.Center;
            footer.Children.Add(_countdown);

            Button close = new Button();
            close.Content = "立刻退出";
            close.FontFamily = Font;
            close.FontSize = 28 * scale;
            close.FontWeight = FontWeights.Bold;
            close.Foreground = WhiteBrush;
            close.Background = AmberDarkBrush;
            close.BorderBrush = AmberBrush;
            close.BorderThickness = new Thickness(2);
            close.Padding = new Thickness(54 * scale, 12 * scale, 54 * scale, 12 * scale);
            close.Margin = new Thickness(0, 12 * scale, 0, 0);
            close.HorizontalAlignment = HorizontalAlignment.Center;
            close.Cursor = System.Windows.Input.Cursors.Hand;
            close.Click += delegate { RequestPasswordClose(); };
            footer.Children.Add(close);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            UpdateCountdown();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += delegate
            {
                _remainingSeconds--;
                if (_remainingSeconds <= 0) { ForceClose(); return; }
                UpdateCountdown();
                if (!_passwordDialogOpen)
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero) Win32.KeepTopmost(hwnd);
                }
            };

            Loaded += delegate
            {
                ActivateTopmost();
                Focus();
            };
            StateChanged += delegate
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            };
            Closing += OnClosing;
            Closed += delegate
            {
                _timer.Stop();
                if (_closedRaised) return;
                _closedRaised = true;
                Action handler = ReminderClosed;
                if (handler != null) handler();
            };
        }

        public void ShowReminder()
        {
            Show();
            ActivateTopmost();
            _timer.Start();
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        private void RequestPasswordClose()
        {
            if (_passwordDialogOpen) return;
            _passwordDialogOpen = true;
            PasswordWindow password = new PasswordWindow("退出任务提醒");
            password.Owner = this;
            bool passed = password.ShowDialog() == true && password.Passed;
            _passwordDialogOpen = false;
            if (passed) ForceClose();
            else ActivateTopmost();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!_allowClose) e.Cancel = true;
        }

        private void UpdateCountdown()
        {
            int minutes = _remainingSeconds / 60;
            int seconds = _remainingSeconds % 60;
            _countdown.Text = minutes + " 分 " + seconds.ToString("00") + " 秒后自动退出  ·  立刻退出需填写管理密码";
        }

        private void ActivateTopmost()
        {
            Topmost = false;
            Topmost = true;
            Activate();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Win32.KeepTopmost(hwnd);
        }

        private static Border MakeCard(double scale)
        {
            Border card = new Border();
            card.Background = new SolidColorBrush(Color.FromRgb(41, 37, 36));
            card.CornerRadius = new CornerRadius(16 * scale);
            card.Padding = new Thickness(30 * scale, 20 * scale, 30 * scale, 20 * scale);
            card.Margin = new Thickness(0, 8 * scale, 0, 8 * scale);
            return card;
        }

        private static TextBlock MakeText(string text, double size, Brush brush, FontWeight weight)
        {
            TextBlock block = new TextBlock();
            block.Text = text;
            block.FontFamily = Font;
            block.FontSize = size;
            block.Foreground = brush;
            block.FontWeight = weight;
            block.TextWrapping = TextWrapping.Wrap;
            return block;
        }
    }
}
