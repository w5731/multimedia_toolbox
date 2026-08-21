using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MultimediaClient
{
    /// <summary>Win32 API:窗口样式穿透、壁纸层嵌入</summary>
    internal static class Win32
    {
        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int WS_EX_TRANSPARENT = 0x00000020;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
            string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam,
            IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint GW_HWNDNEXT = 2;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPOS
        {
            internal IntPtr hwnd;
            internal IntPtr hwndInsertAfter;
            internal int x;
            internal int y;
            internal int cx;
            internal int cy;
            internal uint flags;
        }

        /// <summary>
        /// 返回 z-order 中紧贴桌面壁纸窗口(Progman)上方的那个窗口句柄。
        /// 把看板放到它下面,看板就永远位于壁纸之上、所有应用窗口之下。
        /// 找不到(如非交互桌面)返回 Zero,调用方保持原状。
        /// </summary>
        internal static IntPtr GetWindowAboveDesktop(IntPtr self)
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            IntPtr above = IntPtr.Zero;
            IntPtr cur = GetTopWindow(IntPtr.Zero);
            while (cur != IntPtr.Zero && cur != progman)
            {
                if (cur != self) above = cur;
                cur = GetWindow(cur, GW_HWNDNEXT);
            }
            if (cur != progman) return IntPtr.Zero;
            return above;
        }

        /// <summary>
        /// 让窗口不可激活、不出现在任务栏和 Alt+Tab。
        /// 注意:不能给顶层窗口加 WS_EX_TRANSPARENT —— 该样式会让顶层窗口
        /// 排在桌面壁纸窗口之后绘制(看板被壁纸盖住)。鼠标穿透改用
        /// WM_NCHITTEST 返回 HTTRANSPARENT 实现(见 OverlayWindow)。
        /// </summary>
        internal static void MakeClickThrough(IntPtr hwnd)
        {
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                ex | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        /// <summary>
        /// 尝试把窗口挂到桌面壁纸层(图标之下)。成功返回 true;失败(如经典主题)返回 false,调用方保持普通模式。
        /// 原理:向 Progman 发送 0x052C 让系统在图标层下方分离出一个 WorkerW。
        /// </summary>
        internal static bool TryAttachToWallpaperLayer(IntPtr hwnd)
        {
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                if (progman == IntPtr.Zero) return false;
                IntPtr result;
                SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1500, out result);

                IntPtr workerW = IntPtr.Zero;
                EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    IntPtr defView = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero)
                    {
                        // 图标所在的 WorkerW 的下一个同级 WorkerW 即为壁纸层
                        workerW = FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
                    }
                    return true;
                }, IntPtr.Zero);

                if (workerW == IntPtr.Zero) return false;
                return SetParent(hwnd, workerW) != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }
}
