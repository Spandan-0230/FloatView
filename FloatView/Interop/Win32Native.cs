using System.Runtime.InteropServices;
using System.Text;

namespace FloatView.Interop;

/// <summary>
/// Win32 interop used by FloatView. Signatures follow the documented
/// user32/shcore API contracts.
/// </summary>
public static class Win32Native
{
    // Window messages
    public const int WM_GETICON = 0x007F;
    public const int WM_SIZING = 0x0214;
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_APPCOMMAND = 0x0319;

    // WM_APPCOMMAND commands
    public const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;

    // Icon types for WM_GETICON
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const int ICON_SMALL2 = 2;

    // GetWindowLongPtr indexes
    public const int GWL_EXSTYLE = -20;
    public const int GCLP_HICON = -14;
    public const int GCLP_HICONSM = -34;

    // Window extended styles
    public const long WS_EX_TOOLWINDOW = 0x00000080L;

    // ShowWindow commands
    public const int SW_RESTORE = 9;
    public const int SW_SHOWNOACTIVATE = 4;

    // Virtual key codes
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_SPACE = 0x20;

    // SetWindowPos flags
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    // MonitorFromWindow flags
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    // WinEvent hook events
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;

    // WinEvent hook flags
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    // SendMessageTimeout flags
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public RECT(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate callback,
        uint idProcess,
        uint idThread,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Returns the window rect in physical pixels.</summary>
    public static bool TryGetWindowRect(IntPtr hWnd, out RECT rect) => GetWindowRect(hWnd, out rect);

    /// <summary>Returns the work area (physical pixels) of the monitor nearest the window.</summary>
    public static RECT GetMonitorWorkArea(IntPtr hWnd)
    {
        var monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(monitor, ref info))
                return info.rcWork;
        }

        return GetPrimaryWorkArea();
    }

    private static RECT GetPrimaryWorkArea()
    {
        var monitor = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(monitor, ref info))
                return info.rcWork;
        }

        return new RECT(0, 0, 1920, 1080);
    }
}
