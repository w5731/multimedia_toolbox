using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MultimediaClient
{
    /// <summary>
    /// 壁纸式任务看板:无边框透明窗口,鼠标穿透、不可激活,
    /// 打开的应用窗口会自然遮挡它,观感如同壁纸的一部分。
    /// </summary>
    internal class OverlayWindow : Window
    {
        private static readonly Brush BrushPanel = new SolidColorBrush(Color.FromArgb(190, 16, 22, 40));
        private static readonly Brush BrushWhite = Brushes.White;
        private static readonly Brush BrushDim = new SolidColorBrush(Color.FromRgb(160, 170, 190));
        private static readonly Brush BrushCyan = new SolidColorBrush(Color.FromRgb(77, 208, 225));
        private static readonly Brush BrushAmber = new SolidColorBrush(Color.FromRgb(255, 202, 40));
        private static readonly Brush BrushAmberBg = new SolidColorBrush(Color.FromArgb(64, 255, 193, 7));
        private static readonly Brush BrushRed = new SolidColorBrush(Color.FromRgb(239, 83, 80));
        private static readonly FontFamily Font = new FontFamily("Microsoft YaHei");

        private readonly DispatcherTimer _timer;
        private Border _noticeBar;
        private TextBlock _noticeText;
        private TextBlock _clockText;
        private TextBlock _dateText;
        private TextBlock _offlineText;
        private StackPanel _tasksPanel;
        private TextBlock _countdownText;
        private string _position = "right";
        private double _scale = 1.0;
        private int _lastRebuildMinute = -1;
        private DateTime _lastRebuildDate = DateTime.MinValue;

        public OverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = false;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;

            DataStore.Updated += delegate { Dispatcher.BeginInvoke(new Action(Rebuild)); };
            StateChanged += delegate
            {
                // 抵抗 Win+D 显示桌面:被最小化时立即恢复
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            };

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += delegate { Tick(); };
            _timer.Start();
        }

        /// <summary>应用服务器下发的位置与字号设置,并整体重建界面</summary>
        public void ApplySettings()
        {
            string pos = DataStore.Settings.OverlayPosition;
            double scale = DataStore.Settings.FontScale;
            _position = pos;
            _scale = scale;
            BuildLayout();
            Rebuild();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Win32.MakeClickThrough(hwnd);
            // 鼠标穿透:命中测试一律返回 HTTRANSPARENT,点击直达桌面
            HwndSource source = HwndSource.FromHwnd(hwnd);
            if (source != null) source.AddHook(WndProc);
            if (Config.OverlayMode == "wallpaper")
            {
                Win32.TryAttachToWallpaperLayer(hwnd);
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084;
            const int WM_WINDOWPOSCHANGING = 0x0046;
            if (msg == WM_NCHITTEST)
            {
                handled = true;
                return new IntPtr(-1); // HTTRANSPARENT
            }
            if (msg == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
            {
                // 看板要像壁纸一样待在所有应用窗口之下:每次 z-order 变化
                // (启动、Win+D 恢复等)都强制重新落到壁纸窗口正上方
                Win32.WINDOWPOS pos = (Win32.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Win32.WINDOWPOS));
                IntPtr anchor = Win32.GetWindowAboveDesktop(hwnd);
                if (anchor != IntPtr.Zero && pos.hwndInsertAfter != anchor)
                {
                    pos.hwndInsertAfter = anchor;
                    pos.flags &= ~Win32.SWP_NOZORDER;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        // ---------------- 布局 ----------------

        private void BuildLayout()
        {
            Rect wa = SystemParameters.WorkArea;

            Border root = new Border();
            root.Background = BrushPanel;
            root.CornerRadius = new CornerRadius(16);
            root.Padding = new Thickness(26 * _scale, 18 * _scale, 26 * _scale, 18 * _scale);

            StackPanel panel = new StackPanel();
            root.Child = panel;

            // 通知栏(有通知时可见)
            _noticeBar = new Border();
            _noticeBar.Background = new SolidColorBrush(Color.FromArgb(220, 255, 143, 0));
            _noticeBar.CornerRadius = new CornerRadius(8);
            _noticeBar.Padding = new Thickness(14, 8, 14, 8);
            _noticeBar.Margin = new Thickness(0, 0, 0, 12);
            _noticeText = new TextBlock();
            _noticeText.Foreground = new SolidColorBrush(Color.FromRgb(40, 24, 0));
            _noticeText.FontFamily = Font;
            _noticeText.FontWeight = FontWeights.Bold;
            _noticeText.FontSize = 24 * _scale;
            _noticeText.TextWrapping = TextWrapping.Wrap;
            _noticeBar.Child = _noticeText;
            _noticeBar.Visibility = Visibility.Collapsed;
            panel.Children.Add(_noticeBar);

            // 时钟
            _clockText = MakeText(56 * _scale, BrushWhite, FontWeights.Bold);
            panel.Children.Add(_clockText);

            // 日期 + 星期 + 班级
            _dateText = MakeText(20 * _scale, BrushDim, FontWeights.Normal);
            panel.Children.Add(_dateText);

            // 离线提示
            _offlineText = MakeText(16 * _scale, BrushRed, FontWeights.Bold);
            _offlineText.Visibility = Visibility.Collapsed;
            _offlineText.Margin = new Thickness(0, 4, 0, 0);
            _offlineText.Text = "● 离线(显示缓存内容)";
            panel.Children.Add(_offlineText);

            // 分隔线
            Border divider = new Border();
            divider.Height = 2;
            divider.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            divider.Margin = new Thickness(0, 14 * _scale, 0, 14 * _scale);
            panel.Children.Add(divider);

            // 任务列表(可滚动,任务多时不会撑爆屏幕)
            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _tasksPanel = new StackPanel();
            scroller.Content = _tasksPanel;
            panel.Children.Add(scroller);

            // 下一项倒计时
            _countdownText = MakeText(20 * _scale, BrushAmber, FontWeights.Bold);
            _countdownText.Margin = new Thickness(0, 14 * _scale, 0, 0);
            panel.Children.Add(_countdownText);

            Content = root;

            // 位置与尺寸
            if (_position == "top")
            {
                Width = wa.Width * 0.92;
                SizeToContent = SizeToContent.Height;
                Left = wa.Left + (wa.Width - Width) / 2;
                Top = wa.Top + 14;
            }
            else
            {
                double w = Math.Max(420 * _scale, wa.Width * 0.26);
                Width = w;
                // 固定高度:透明窗口的 SizeToContent 在部分系统上不可靠
                Height = wa.Height * 0.86;
                SizeToContent = SizeToContent.Manual;
                Left = _position == "left" ? wa.Left + 18 : wa.Right - w - 18;
                Top = wa.Top + wa.Height * 0.07;
            }
        }

        private TextBlock MakeText(double size, Brush brush, FontWeight weight)
        {
            TextBlock t = new TextBlock();
            t.FontFamily = Font;
            t.FontSize = size;
            t.Foreground = brush;
            t.FontWeight = weight;
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        // ---------------- 数据刷新 ----------------

        private void Tick()
        {
            DateTime now = TimeSync.Now;
            _clockText.Text = now.ToString("HH:mm:ss");
            // 每分钟(或跨天)重建任务列表;秒针跳动只更新时钟
            if (now.Minute != _lastRebuildMinute || now.Date != _lastRebuildDate)
            {
                _lastRebuildMinute = now.Minute;
                _lastRebuildDate = now.Date;
                Rebuild();
            }
        }

        public void Rebuild()
        {
            DateTime now = TimeSync.Now;
            string[] weekNames = { "日", "一", "二", "三", "四", "五", "六" };
            string dateLine = now.ToString("yyyy年M月d日") + "  星期" + weekNames[(int)now.DayOfWeek];
            if (Config.ClassName.Length > 0) dateLine += "  ·  " + Config.ClassName;
            _dateText.Text = dateLine;

            // 通知栏
            if (DataStore.Notice.Enabled && DataStore.Notice.Text.Length > 0)
            {
                _noticeText.Text = "通知:" + DataStore.Notice.Text;
                _noticeBar.Visibility = Visibility.Visible;
            }
            else
            {
                _noticeBar.Visibility = Visibility.Collapsed;
            }

            // 今日任务
            List<TaskItem> today = new List<TaskItem>();
            foreach (TaskItem t in DataStore.Tasks)
            {
                if (t.AppliesOn(now.Date)) today.Add(t);
            }
            today.Sort(delegate (TaskItem a, TaskItem b)
            {
                return String.CompareOrdinal(a.StartTime, b.StartTime);
            });

            _tasksPanel.Children.Clear();
            if (today.Count == 0)
            {
                TextBlock empty = MakeText(24 * _scale, BrushDim, FontWeights.Normal);
                empty.Text = "今日暂无任务";
                _tasksPanel.Children.Add(empty);
            }
            else
            {
                foreach (TaskItem t in today)
                {
                    _tasksPanel.Children.Add(BuildTaskRow(t, now));
                }
            }

            // 下一项倒计时
            TaskItem next = null;
            foreach (TaskItem t in today)
            {
                if (t.StartOn(now.Date) > now) { next = t; break; }
            }
            if (next != null)
            {
                TimeSpan remain = next.StartOn(now.Date) - now;
                _countdownText.Text = "接下来  " + next.StartTime + "  " + next.Title
                    + "  ·  还有 " + FormatRemain(remain);
                _countdownText.Visibility = Visibility.Visible;
            }
            else if (today.Count > 0)
            {
                _countdownText.Text = "今日任务已全部完成";
                _countdownText.Visibility = Visibility.Visible;
            }
            else
            {
                _countdownText.Visibility = Visibility.Collapsed;
            }
        }

        private UIElement BuildTaskRow(TaskItem t, DateTime now)
        {
            bool active = t.IsActiveNow(now);
            bool past = !active && t.StartOn(now.Date) < now
                && (!t.IsRange || DateTime.ParseExact(now.Date.ToString("yyyy-MM-dd") + " " + t.EndTime,
                    "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) < now);

            Border row = new Border();
            row.CornerRadius = new CornerRadius(8);
            row.Padding = new Thickness(12, 7 * _scale, 12, 7 * _scale);
            row.Margin = new Thickness(0, 0, 0, 6 * _scale);
            if (active) row.Background = BrushAmberBg;

            // 两列网格:时间列自适应宽度,内容列占满剩余空间并自动换行
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock time = MakeText(26 * _scale, active ? BrushAmber : BrushCyan, FontWeights.Bold);
            time.Text = t.TimeText;
            time.TextWrapping = TextWrapping.NoWrap;
            time.Margin = new Thickness(0, 0, 14, 0);
            time.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(time, 0);
            grid.Children.Add(time);

            TextBlock title = MakeText(26 * _scale,
                past ? BrushDim : BrushWhite, active ? FontWeights.Bold : FontWeights.Normal);
            if (past) title.Opacity = 0.55;
            title.Inlines.Add(new System.Windows.Documents.Run(t.Title));
            if (active)
            {
                // “进行中”作为同行内联标签,随内容自动换行,不会溢出
                System.Windows.Documents.Run tagRun = new System.Windows.Documents.Run("  [进行中]");
                tagRun.Foreground = BrushAmber;
                tagRun.FontSize = 18 * _scale;
                tagRun.FontWeight = FontWeights.Bold;
                title.Inlines.Add(tagRun);
            }
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            row.Child = grid;
            return row;
        }

        private static string FormatRemain(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return (int)ts.TotalHours + " 小时 " + ts.Minutes + " 分";
            return Math.Max(1, (int)Math.Ceiling(ts.TotalMinutes)) + " 分钟";
        }

        public void SetOffline(bool offline)
        {
            _offlineText.Visibility = offline ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
