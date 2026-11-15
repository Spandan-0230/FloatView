using System.Diagnostics;
using FloatView.Interop;

namespace FloatView.Services;

/// <summary>
/// Sends media commands to the source window without activating it.
///
/// Presenting the window to the foreground is deliberately avoided: switching
/// focus is what made seeking look like the video file was being reopened.
/// Chromium browsers route posted keyboard messages to the focused tab, while
/// VLC and most native players honour <c>WM_APPCOMMAND</c>.
/// </summary>
public static class MediaController
{
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "vivaldi",
        "chromium",
        "thorium",
        "librewolf",
        "waterfox",
    };

    // Scan codes used in the lParam of posted keyboard messages.
    private const int ScanLeft = 0x4B;
    private const int ScanRight = 0x4D;
    private const int ScanSpace = 0x39;

    /// <summary>
    /// Toggles play/pause in the source. Browsers receive a posted Space
    /// (the shortcut used by YouTube and most web players); native players
    /// receive <c>APPCOMMAND_MEDIA_PLAY_PAUSE</c>.
    /// </summary>
    public static void TogglePlayPause(IntPtr source)
    {
        if (!Win32Native.IsWindow(source))
            return;

        try
        {
            if (IsBrowser(source))
            {
                AppLog.Info("Media play/pause (browser Space)");
                PostKey(source, Win32Native.VK_SPACE, ScanSpace, extended: false);
            }
            else
            {
                AppLog.Info("Media play/pause (WM_APPCOMMAND)");
                PostAppCommand(source, Win32Native.APPCOMMAND_MEDIA_PLAY_PAUSE);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Play/pause command failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Seeks the source player by posting arrow keys. Each press maps to the
    /// player's own step (5 seconds in browsers, VLC's short jump otherwise).
    /// </summary>
    public static async Task SeekAsync(IntPtr source, bool forward, int presses)
    {
        if (!Win32Native.IsWindow(source) || Win32Native.IsIconic(source))
            return;

        presses = Math.Clamp(presses, 1, 10);
        var vk = forward ? Win32Native.VK_RIGHT : Win32Native.VK_LEFT;
        var scan = forward ? ScanRight : ScanLeft;

        AppLog.Info($"Media seek {(forward ? "forward" : "back")} x{presses}");

        for (var i = 0; i < presses; i++)
        {
            PostKey(source, vk, scan, extended: true);
            if (i < presses - 1)
                await Task.Delay(60);
        }
    }

    private static bool IsBrowser(IntPtr source)
    {
        try
        {
            Win32Native.GetWindowThreadProcessId(source, out var pid);
            using var process = Process.GetProcessById((int)pid);
            return BrowserProcesses.Contains(process.ProcessName);
        }
        catch
        {
            return false;
        }
    }

    private static void PostAppCommand(IntPtr source, int command)
    {
        var ok = Win32Native.PostMessage(
            source,
            Win32Native.WM_APPCOMMAND,
            source,
            (IntPtr)(command << 16));

        if (!ok)
            AppLog.Warn($"PostMessage WM_APPCOMMAND {command} failed: 0x{System.Runtime.InteropServices.Marshal.GetLastWin32Error():X8}");
    }

    private static void PostKey(IntPtr target, ushort virtualKey, int scanCode, bool extended)
    {
        const int repeat = 1;
        var extendedBit = extended ? 1L << 24 : 0;

        var down = repeat | ((long)scanCode << 16) | extendedBit;
        var up = down | (1L << 30) | (1L << 31);

        var downOk = Win32Native.PostMessage(target, Win32Native.WM_KEYDOWN, (IntPtr)virtualKey, (IntPtr)down);
        var upOk = Win32Native.PostMessage(target, Win32Native.WM_KEYUP, (IntPtr)virtualKey, (IntPtr)up);

        AppLog.Info(
            $"Posted key 0x{virtualKey:X2} scan=0x{scanCode:X2} ext={extended} to 0x{target:X} " +
            $"down={downOk} up={upOk} fg=0x{Win32Native.GetForegroundWindow():X}");

        if (!downOk)
            AppLog.Warn($"PostMessage WM_KEYDOWN 0x{virtualKey:X2} failed");

        if (!upOk)
            AppLog.Warn($"PostMessage WM_KEYUP 0x{virtualKey:X2} failed");
    }
}
