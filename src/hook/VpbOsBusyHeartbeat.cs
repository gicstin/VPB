using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace VPB
{
    /// <summary>
    /// Topmost Win32 strip pulsed off the Unity main thread.
    /// Only path that keeps moving while Unity is stuck in sync work (Tier B).
    /// </summary>
    internal static class VpbOsBusyHeartbeat
    {
        private const string WindowClassName = "VPB_OsBusyHeartbeat";
        private const int StripHeightPx = 4;
        private const int TimerId = 1;
        private const int TimerMs = 33;
        private const int WmDestroy = 0x0002;
        private const int WmPaint = 0x000F;
        private const int WmTimer = 0x0113;
        private const int WmClose = 0x0010;
        private const int WmEraseBkgnd = 0x0014;
        private const int WmUserHide = 0x0400 + 77;
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsVisible = 0x10000000;
        private const int WsExTopmost = 0x00000008;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExTransparent = 0x00000020;
        private const int WsExLayered = 0x00080000;
        private const int SwHide = 0;
        private const int SwShowNoActivate = 4;
        private const int HwndTopmost = -1;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private const uint SwpHideWindow = 0x0080;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const byte LwaAlpha = 0x02;

        private static readonly object s_Sync = new object();
        private static Thread s_Thread;
        private static volatile bool s_WantVisible;
        private static volatile bool s_ThreadRunning;
        private static volatile bool s_ShutdownRequested;
        private static volatile IntPtr s_Hwnd;
        private static int s_Pulse;
        private static IntPtr s_BrushBg;
        private static IntPtr s_BrushFill;
        private static bool s_ClassRegistered;
        // Keep alive — RegisterClass stores unmanaged fn ptr; GC must not collect delegate.
        private static WndProcDelegate s_WndProcKeepAlive;

        internal static void Show()
        {
            if (s_ShutdownRequested) return;
            s_WantVisible = true;
            EnsureThread();
        }

        internal static void Hide()
        {
            s_WantVisible = false;
            IntPtr hwnd = s_Hwnd;
            if (hwnd != IntPtr.Zero)
            {
                try { PostMessage(hwnd, WmUserHide, IntPtr.Zero, IntPtr.Zero); } catch { }
                try { ShowWindow(hwnd, SwHide); } catch { }
            }
        }

        /// <summary>
        /// Tear down Win32 message pump. Hide alone is not enough — GetMessage thread
        /// + HWND keep VaM from exiting cleanly after any EnterBlocking (scene load).
        /// Only PostMessage from foreign threads — DestroyWindow runs on STA owner via WndProc.
        /// </summary>
        internal static void Shutdown()
        {
            s_ShutdownRequested = true;
            s_WantVisible = false;
            Thread thread;
            IntPtr hwnd;
            lock (s_Sync)
            {
                thread = s_Thread;
                hwnd = s_Hwnd;
            }

            if (hwnd != IntPtr.Zero)
            {
                try { PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero); } catch { }
            }

            if (thread != null && thread.IsAlive)
            {
                // Window may still be creating — brief retry so PostMessage can land.
                for (int i = 0; i < 3 && thread.IsAlive; i++)
                {
                    hwnd = s_Hwnd;
                    if (hwnd != IntPtr.Zero)
                    {
                        try { PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero); } catch { }
                        break;
                    }
                    try { Thread.Sleep(50); } catch { }
                }
                try { thread.Join(750); } catch { }
            }

            lock (s_Sync)
            {
                if (s_Thread == thread)
                {
                    s_Thread = null;
                    s_ThreadRunning = false;
                }
                if (s_Hwnd == hwnd)
                    s_Hwnd = IntPtr.Zero;
            }
        }

        private static void EnsureThread()
        {
            if (s_ShutdownRequested) return;
            lock (s_Sync)
            {
                if (s_ShutdownRequested) return;
                if (s_Thread != null && s_ThreadRunning) return;
                s_ThreadRunning = true;
                s_Thread = new Thread(ThreadMain);
                s_Thread.IsBackground = true;
                s_Thread.Name = "VPB_OsBusyHeartbeat";
                try { s_Thread.SetApartmentState(ApartmentState.STA); } catch { }
                s_Thread.Start();
            }
        }

        private static void ThreadMain()
        {
            try
            {
                if (s_ShutdownRequested) return;

                EnsureWindowClass();
                s_BrushBg = CreateSolidBrush(Rgb(18, 22, 28));
                s_BrushFill = CreateSolidBrush(Rgb(90, 200, 255));

                int screenW = GetSystemMetrics(0);
                if (screenW < 320) screenW = 1920;

                IntPtr hwnd = CreateWindowEx(
                    WsExTopmost | WsExToolWindow | WsExNoActivate | WsExTransparent | WsExLayered,
                    WindowClassName,
                    "VPB Busy",
                    WsPopup,
                    0, 0, screenW, StripHeightPx,
                    IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
                s_Hwnd = hwnd;
                if (hwnd == IntPtr.Zero)
                {
                    s_ThreadRunning = false;
                    return;
                }

                if (s_ShutdownRequested)
                {
                    try { DestroyWindow(hwnd); } catch { }
                    return;
                }

                SetLayeredWindowAttributes(hwnd, 0, 230, LwaAlpha);
                SetWindowPos(hwnd, new IntPtr(HwndTopmost), 0, 0, screenW, StripHeightPx,
                    SwpNoActivate | SwpHideWindow);
                SetTimer(hwnd, new IntPtr(TimerId), TimerMs, IntPtr.Zero);

                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (s_ShutdownRequested)
                    {
                        try { DestroyWindow(hwnd); } catch { }
                        break;
                    }
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch
            {
                // Heartbeat is best-effort; never throw into Unity.
            }
            finally
            {
                try
                {
                    if (s_BrushBg != IntPtr.Zero) DeleteObject(s_BrushBg);
                    if (s_BrushFill != IntPtr.Zero) DeleteObject(s_BrushFill);
                }
                catch { }
                s_BrushBg = IntPtr.Zero;
                s_BrushFill = IntPtr.Zero;
                s_Hwnd = IntPtr.Zero;
                s_ThreadRunning = false;
            }
        }

        private static void EnsureWindowClass()
        {
            if (s_ClassRegistered) return;
            s_WndProcKeepAlive = WndProc;
            var wc = new WNDCLASS();
            wc.style = 0;
            wc.lpfnWndProc = s_WndProcKeepAlive;
            wc.cbClsExtra = 0;
            wc.cbWndExtra = 0;
            wc.hInstance = GetModuleHandle(null);
            wc.hIcon = IntPtr.Zero;
            wc.hCursor = IntPtr.Zero;
            wc.hbrBackground = IntPtr.Zero;
            wc.lpszMenuName = null;
            wc.lpszClassName = WindowClassName;
            RegisterClass(ref wc);
            s_ClassRegistered = true;
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WmTimer)
                {
                    if (s_WantVisible)
                    {
                        s_Pulse = (s_Pulse + 1) % 1000;
                        if (!IsWindowVisible(hWnd))
                            ShowWindow(hWnd, SwShowNoActivate);
                        InvalidateRect(hWnd, IntPtr.Zero, false);
                    }
                    else if (IsWindowVisible(hWnd))
                    {
                        ShowWindow(hWnd, SwHide);
                    }
                    return IntPtr.Zero;
                }

                if (msg == WmUserHide)
                {
                    s_WantVisible = false;
                    ShowWindow(hWnd, SwHide);
                    return IntPtr.Zero;
                }

                if (msg == WmEraseBkgnd)
                    return new IntPtr(1);

                if (msg == WmPaint)
                {
                    IntPtr hdc = GetDC(hWnd);
                    if (hdc != IntPtr.Zero)
                    {
                        try { PaintStrip(hdc, hWnd); } catch { }
                        ReleaseDC(hWnd, hdc);
                    }
                    ValidateRect(hWnd, IntPtr.Zero);
                    return IntPtr.Zero;
                }

                if (msg == WmClose)
                {
                    DestroyWindow(hWnd);
                    return IntPtr.Zero;
                }

                if (msg == WmDestroy)
                {
                    KillTimer(hWnd, new IntPtr(TimerId));
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                }
            }
            catch { }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private static void PaintStrip(IntPtr hdc, IntPtr hWnd)
        {
            RECT rc;
            GetClientRect(hWnd, out rc);
            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            if (w <= 0 || h <= 0) return;

            FillRect(hdc, ref rc, s_BrushBg);

            int barW = Math.Max(48, w / 5);
            int travel = Math.Max(1, w - barW);
            int x = (s_Pulse * travel) / 60;
            if (x > travel) x = x % (travel + 1);

            RECT fill = new RECT();
            fill.Left = x;
            fill.Top = 0;
            fill.Right = x + barW;
            fill.Bottom = h;
            FillRect(hdc, ref fill, s_BrushFill);
        }

        private static uint Rgb(byte r, byte g, byte b)
        {
            return (uint)(r | (g << 8) | (b << 16));
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, byte dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll")]
        private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

        [DllImport("user32.dll")]
        private static extern bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool ValidateRect(IntPtr hWnd, IntPtr lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint crColor);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
