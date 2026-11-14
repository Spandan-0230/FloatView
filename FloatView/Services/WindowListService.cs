using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using FloatView.Interop;
using FloatView.Models;

namespace FloatView.Services;

/// <summary>
/// Keeps a snapshot of mirrorable top-level windows and raises change events
/// using out-of-context Win event hooks (window create/destroy, foreground).
/// </summary>
public sealed class WindowListService : IDisposable
{
    private readonly IconService _iconService = new();
    private readonly Dictionary<uint, string> _processNames = new();
    private readonly DispatcherTimer _refreshDebounce;

    private Win32Native.WinEventDelegate? _eventCallback;
    private IntPtr _createHook;
    private IntPtr _foregroundHook;
    private bool _disposed;

    public WindowListService()
    {
        _refreshDebounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _refreshDebounce.Tick += (_, _) =>
        {
            _refreshDebounce.Stop();
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? WindowsChanged;

    public event EventHandler<IntPtr>? ForegroundWindowChanged;

    public void Start()
    {
        if (_eventCallback is not null)
            return;

        _eventCallback = OnWinEvent;
        const uint flags = Win32Native.WINEVENT_OUTOFCONTEXT | Win32Native.WINEVENT_SKIPOWNPROCESS;

        _createHook = Win32Native.SetWinEventHook(
            Win32Native.EVENT_OBJECT_CREATE,
            Win32Native.EVENT_OBJECT_HIDE,
            IntPtr.Zero,
            _eventCallback,
            0,
            0,
            flags);

        _foregroundHook = Win32Native.SetWinEventHook(
            Win32Native.EVENT_SYSTEM_FOREGROUND,
            Win32Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _eventCallback,
            0,
            0,
            flags);

        AppLog.Info($"Win event hooks installed (create={_createHook}, foreground={_foregroundHook})");
    }

    /// <summary>Enumerates currently mirrorable windows in Z order (top first).</summary>
    public IReadOnlyList<AppWindow> Snapshot()
    {
        var ownPid = (uint)Environment.ProcessId;
        var shellWindow = Win32Native.GetShellWindow();
        var windows = new List<AppWindow>();

        Win32Native.EnumWindows((hWnd, _) =>
        {
            try
            {
                if (hWnd == shellWindow)
                    return true;

                if (!Win32Native.IsWindowVisible(hWnd))
                    return true;

                Win32Native.GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == 0 || pid == ownPid)
                    return true;

                if (DwmNative.IsCloaked(hWnd))
                    return true;

                var exStyle = Win32Native.GetWindowLongPtr(hWnd, Win32Native.GWL_EXSTYLE).ToInt64();
                if ((exStyle & Win32Native.WS_EX_TOOLWINDOW) != 0)
                    return true;

                var titleLength = Win32Native.GetWindowTextLengthW(hWnd);
                if (titleLength == 0)
                    return true;

                if (!Win32Native.GetWindowRect(hWnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
                    return true;

                var title = ReadTitle(hWnd, titleLength);
                windows.Add(new AppWindow(hWnd, title, GetProcessName(pid), _iconService.TryGetIcon(hWnd)));
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Skipping window 0x{hWnd.ToInt64():X}: {ex.Message}");
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public static string GetProcessNameSafe(uint pid)
    {
        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string ReadTitle(IntPtr hWnd, int length)
    {
        var buffer = new StringBuilder(length + 1);
        Win32Native.GetWindowTextW(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private string GetProcessName(uint pid)
    {
        if (_processNames.TryGetValue(pid, out var name))
            return name;

        name = GetProcessNameSafe(pid);
        _processNames[pid] = name;

        if (_processNames.Count > 512)
            _processNames.Clear();

        return name;
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_disposed)
            return;

        if (eventType == Win32Native.EVENT_SYSTEM_FOREGROUND)
        {
            if (hwnd != IntPtr.Zero)
                ForegroundWindowChanged?.Invoke(this, hwnd);
            return;
        }

        if (idObject != 0 || idChild != 0)
            return;

        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _refreshDebounce.Stop();

        if (_createHook != IntPtr.Zero)
            Win32Native.UnhookWinEvent(_createHook);
        if (_foregroundHook != IntPtr.Zero)
            Win32Native.UnhookWinEvent(_foregroundHook);

        _createHook = IntPtr.Zero;
        _foregroundHook = IntPtr.Zero;
        _eventCallback = null;
    }
}
