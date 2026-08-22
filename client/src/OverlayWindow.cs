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
        // 暖色调色板(与教师端网站一致):深石灰底 + 琥珀点缀,远看柔和不刺眼
        private static readonly Brush BrushPanel = new SolidColorBrush(Color.FromArgb(208, 28, 25, 23));
        private static readonly Brush BrushWhite = new SolidColorBrush(Color.FromRgb(250, 250, 249));
        private static readonly Brush BrushDim = new SolidColorBrush(Color.FromRgb(168, 162, 158));
        private static readonly Brush BrushRemark = new SolidColorBrush(Color.FromRgb(214, 211, 209));
        private static readonly Brush BrushTime = new SolidColorBrush(Color.FromRgb(252, 211, 77));
        private static readonly Brush BrushAmber = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        private static readonly Brush BrushAmberBg = new SolidColorBrush(Color.FromArgb(46, 251, 191, 36));
        private static readonly Brush BrushRed = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        private static readonly FontFamily Font = new FontFamily("Microsoft YaHei");

        private readonly DispatcherTimer _timer;
        private Border _noticeBar;
        private TextBlock _noticeText;
        private TextBlock _clockText;
        private TextBlock _dateText;
        private TextBlock _offlineText;
        private StackPanel _tasksPanel;
        private ScrollViewer _scroller;
        private List<TaskItem> _todayList = new List<TaskItem>();
        private bool _centerPending;
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
            root.CornerRadius = new CornerRadius(18);
            root.Padding = new Thickness(30 * _scale, 24 * _scale, 30 * _scale, 22 * _scale);

            StackPanel panel = new StackPanel();
            root.Child = panel;

            // 通知栏(有通知时可见)
            _noticeBar = new Border();
            _noticeBar.Background = new SolidColorBrush(Color.FromArgb(235, 245, 158, 11));
            _noticeBar.CornerRadius = new CornerRadius(10);
            _noticeBar.Padding = new Thickness(16, 10, 16, 10);
            _noticeBar.Margin = new Thickness(0, 0, 0, 14);
            _noticeText = new TextBlock();
            _noticeText.Foreground = new SolidColorBrush(Color.FromRgb(41, 37, 36));
            _noticeText.FontFamily = Font;
            _noticeText.FontWeight = FontWeights.Bold;
            _noticeText.FontSize = 26 * _scale;
            _noticeText.TextWrapping = TextWrapping.Wrap;
            _noticeBar.Child = _noticeText;
            _noticeBar.Visibility = Visibility.Collapsed;
            panel.Children.Add(_noticeBar);

            // 时钟
            _clockText = MakeText(84 * _scale, BrushWhite, FontWeights.Bold);
            panel.Children.Add(_clockText);

            // 日期 + 星期 + 班级
            _dateText = MakeText(26 * _scale, BrushDim, FontWeights.Normal);
            _dateText.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(_dateText);

            // 离线提示
            _offlineText = MakeText(20 * _scale, BrushRed, FontWeights.Bold);
            _offlineText.Visibility = Visibility.Collapsed;
            _offlineText.Margin = new Thickness(0, 6, 0, 0);
            _offlineText.Text = "● 离线(显示缓存内容)";
            panel.Children.Add(_offlineText);

            // 分隔线
            Border divider = new Border();
            divider.Height = 1;
            divider.Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));
            divider.Margin = new Thickness(0, 16 * _scale, 0, 12 * _scale);
            panel.Children.Add(divider);

            // 栏目小标题
            TextBlock section = MakeText(22 * _scale, BrushDim, FontWeights.SemiBold);
            section.Text = "今日安排";
            section.Margin = new Thickness(0, 0, 0, 10 * _scale);
            panel.Children.Add(section);

            // 任务列表(可滚动,任务多时不会撑爆屏幕)
            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _tasksPanel = new StackPanel();
            scroller.Content = _tasksPanel;
            // 布局完成后(RenderSize 有效)再执行待定的居中滚动
            scroller.LayoutUpdated += delegate
            {
                if (_centerPending)
                {
                    _centerPending = false;
                    CenterOnActive();
                }
            };
            _scroller = scroller;
            panel.Children.Add(scroller);

            // 下一项倒计时
            _countdownText = MakeText(26 * _scale, BrushAmber, FontWeights.Bold);
            _countdownText.Margin = new Thickness(0, 14 * _scale, 0, 0);
            panel.Children.Add(_countdownText);

            Content = root;

            // 位置与尺寸
            if (_position == "top")
            {
                Width = wa.Width * 0.94;
                SizeToContent = SizeToContent.Height;
                Left = wa.Left + (wa.Width - Width) / 2;
                Top = wa.Top + 14;
            }
            else
            {
                double w = Math.Max(560 * _scale, wa.Width * 0.30);
                Width = w;
                // 固定高度:透明窗口的 SizeToContent 在部分系统上不可靠
                Height = wa.Height * 0.88;
                SizeToContent = SizeToContent.Manual;
                Left = _position == "left" ? wa.Left + 18 : wa.Right - w - 18;
                Top = wa.Top + wa.Height * 0.06;
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
            _todayList = today;
            if (today.Count == 0)
            {
                TextBlock empty = MakeText(30 * _scale, BrushDim, FontWeights.Normal);
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
            // 布局完成后(LayoutUpdated)把当前进行中(或下一个)的任务滚动到列表中间
            _centerPending = true;

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

        /// <summary>
        /// 任务列表超出可视高度时,自动滚动到"焦点任务"上下居中:
        /// 优先当前进行中的任务;没有进行中的则选下一个即将开始的。
        /// 看板不可交互(鼠标穿透),所以始终自动定位,无需人工滚动。
        /// </summary>
        private void CenterOnActive()
        {
            try
            {
                if (_scroller == null || _tasksPanel == null) return;
                if (_scroller.ScrollableHeight <= 0) return; // 列表全部可见,无需滚动
                DateTime now = TimeSync.Now;
                int idx = -1;
                for (int i = 0; i < _todayList.Count && i < _tasksPanel.Children.Count; i++)
                {
                    if (_todayList[i].IsActiveNow(now)) { idx = i; break; }
                }
                if (idx < 0)
                {
                    for (int i = 0; i < _todayList.Count && i < _tasksPanel.Children.Count; i++)
                    {
                        if (_todayList[i].StartOn(now.Date) > now) { idx = i; break; }
                    }
                }
                if (idx < 0) idx = _tasksPanel.Children.Count - 1; // 都已过时,看最后一项
                double top = 0;
                for (int i = 0; i < idx; i++)
                {
                    top += _tasksPanel.Children[i].RenderSize.Height;
                    FrameworkElement fe = _tasksPanel.Children[i] as FrameworkElement;
                    if (fe != null) top += fe.Margin.Top + fe.Margin.Bottom;
                }
                FrameworkElement target = _tasksPanel.Children[idx] as FrameworkElement;
                if (target == null) return;
                double center = top + target.RenderSize.Height / 2;
                double offset = center - _scroller.ViewportHeight / 2;
                if (offset < 0) offset = 0;
                if (offset > _scroller.ScrollableHeight) offset = _scroller.ScrollableHeight;
                _scroller.ScrollToVerticalOffset(offset);
            }
            catch { }
        }

        private UIElement BuildTaskRow(TaskItem t, DateTime now)
        {
            bool active = t.IsActiveNow(now);
            bool past = !active && t.StartOn(now.Date) < now
                && (!t.IsRange || DateTime.ParseExact(now.Date.ToString("yyyy-MM-dd") + " " + t.EndTime,
                    "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) < now);

            Border row = new Border();
            row.CornerRadius = new CornerRadius(10);
            row.Padding = new Thickness(14 * _scale, 10 * _scale, 14 * _scale, 10 * _scale);
            row.Margin = new Thickness(0, 0, 0, 8 * _scale);
            if (active) row.Background = BrushAmberBg;

            StackPanel body = new StackPanel();

            // 第一行:时间 + 项目标题
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock time = MakeText(34 * _scale, active ? BrushAmber : BrushTime, FontWeights.Bold);
            time.Text = t.TimeText;
            time.TextWrapping = TextWrapping.NoWrap;
            time.Margin = new Thickness(0, 0, 16 * _scale, 0);
            time.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(time, 0);
            grid.Children.Add(time);

            TextBlock title = MakeText(34 * _scale,
                past ? BrushDim : BrushWhite, active ? FontWeights.Bold : FontWeights.SemiBold);
            title.Inlines.Add(new System.Windows.Documents.Run(t.Title));
            if (active)
            {
                // “进行中”作为同行内联标签,随内容自动换行,不会溢出
                System.Windows.Documents.Run tagRun = new System.Windows.Documents.Run("  [进行中]");
                tagRun.Foreground = BrushAmber;
                tagRun.FontSize = 24 * _scale;
                tagRun.FontWeight = FontWeights.Bold;
                title.Inlines.Add(tagRun);
            }
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            body.Children.Add(grid);

            // 备注:整行显示,不再挤在时间列右侧,长备注也能充分利用面板宽度
            if (t.Remark.Length > 0)
            {
                TextBlock remark = MakeText(25 * _scale,
                    past ? BrushDim : BrushRemark, FontWeights.Normal);
                remark.Text = "备注:" + t.Remark;
                remark.Margin = new Thickness(0, 6 * _scale, 0, 0);
                body.Children.Add(remark);
            }

            if (past) body.Opacity = 0.55;

            row.Child = body;
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
