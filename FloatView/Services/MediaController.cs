using System.Diagnostics;
using System.Text;
using FloatView.Interop;
using Windows.Media.Control;

namespace FloatView.Services;

/// <summary>
/// Sends media commands to the source window without activating it.
///
/// The primary path uses the Windows System Media Transport Controls (SMTC).
/// Chrome, Edge and other browsers publish their web media sessions there, so
/// YouTube and similar players can be paused and seeked even when keyboard
/// focus sits in the address bar or nothing is focused at all. Native players
/// that do not expose an SMTC session fall back to keyboard messages: posted
/// arrows for seeking and <c>WM_APPCOMMAND</c> for play/pause.
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

    private static GlobalSystemMediaTransportControlsSessionManager? _smtcManager;

    /// <summary>
    /// Toggles play/pause in the source. Uses the Windows media session when
    /// the app exposes one, otherwise a posted Space for browsers or
    /// <c>APPCOMMAND_MEDIA_PLAY_PAUSE</c> for native players.
    /// </summary>
    public static async Task TogglePlayPauseAsync(IntPtr source)
    {
        if (!Win32Native.IsWindow(source))
            return;

        try
        {
            var session = await FindSessionAsync(source);
            if (session is not null && await TrySmtcPlayPauseAsync(session))
            {
                AppLog.Info("Media play/pause (SMTC)");
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("SMTC play/pause failed: " + ex.Message);
        }

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
    /// Seeks the source by five seconds per press. Uses the media session's
    /// timeline when available, otherwise posts the player's own arrow keys.
    /// </summary>
    public static async Task SeekAsync(IntPtr source, bool forward, int presses)
    {
        if (!Win32Native.IsWindow(source) || Win32Native.IsIconic(source))
            return;

        presses = Math.Clamp(presses, 1, 10);

        try
        {
            var session = await FindSessionAsync(source);
            if (session is not null && await TrySmtcSeekAsync(session, forward, presses))
            {
                AppLog.Info($"Media seek {(forward ? "forward" : "back")} x{presses} (SMTC)");
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("SMTC seek failed: " + ex.Message);
        }

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

    private static async Task<bool> TrySmtcPlayPauseAsync(GlobalSystemMediaTransportControlsSession session)
    {
        var status = session.GetPlaybackInfo().PlaybackStatus;
        var ok = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            ? await session.TryPauseAsync()
            : await session.TryPlayAsync();

        if (!ok)
            AppLog.Warn($"SMTC play/pause rejected (status={status})");

        return ok;
    }

    private static async Task<bool> TrySmtcSeekAsync(
        GlobalSystemMediaTransportControlsSession session,
        bool forward,
        int presses)
    {
        var timeline = session.GetTimelineProperties();
        var step = TimeSpan.FromSeconds(5 * presses);
        var target = forward ? timeline.Position + step : timeline.Position - step;

        if (target < TimeSpan.Zero)
            target = TimeSpan.Zero;

        var end = timeline.EndTime;
        if (end > TimeSpan.Zero && target > end)
            target = end;

        var ok = await session.TryChangePlaybackPositionAsync(target.Ticks);
        if (!ok)
            AppLog.Warn("SMTC seek rejected for this session");

        return ok;
    }

    private static async Task<GlobalSystemMediaTransportControlsSession?> FindSessionAsync(IntPtr source)
    {
        var processName = GetProcessName(source);
        if (string.IsNullOrEmpty(processName))
            return null;

        var manager = await GetManagerAsync();
        if (manager is null)
            return null;

        var matches = new List<GlobalSystemMediaTransportControlsSession>();
        foreach (var session in manager.GetSessions())
        {
            if (!AumidMatches(session.SourceAppUserModelId, processName))
                continue;

            matches.Add(session);
        }

        if (matches.Count <= 1)
            return matches.FirstOrDefault();

        // Several sessions from the same app (multiple tabs or profiles):
        // prefer the one actually playing, then the one whose title appears in
        // the mirrored window's title, otherwise the first.
        GlobalSystemMediaTransportControlsSessionPlaybackStatus StatusOf(
            GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                return session.GetPlaybackInfo().PlaybackStatus;
            }
            catch
            {
                return GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
            }
        }

        var playing = matches.FirstOrDefault(s =>
            StatusOf(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        if (playing is not null)
            return playing;

        var title = GetWindowTitle(source);
        if (!string.IsNullOrEmpty(title))
        {
            foreach (var session in matches)
            {
                try
                {
                    var properties = await session.TryGetMediaPropertiesAsync();
                    var mediaTitle = properties?.Title;
                    if (!string.IsNullOrEmpty(mediaTitle) &&
                        title.Contains(mediaTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        return session;
                    }
                }
                catch
                {
                    // Ignore sessions whose metadata cannot be read.
                }
            }
        }

        return matches[0];
    }

    private static async Task<GlobalSystemMediaTransportControlsSessionManager?> GetManagerAsync()
    {
        if (_smtcManager is not null)
            return _smtcManager;

        try
        {
            _smtcManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Windows media session manager unavailable: " + ex.Message);
        }

        return _smtcManager;
    }

    private static bool AumidMatches(string? aumid, string processName)
    {
        if (string.IsNullOrEmpty(aumid))
            return false;

        // Typical AUMIDs: "Chrome.<profile-hash>", "Firefox", "vlc.exe".
        var executable = processName + ".exe";
        return aumid.Contains(processName, StringComparison.OrdinalIgnoreCase)
            || aumid.Contains(executable, StringComparison.OrdinalIgnoreCase)
            || processName.Contains(aumid, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetProcessName(IntPtr source)
    {
        try
        {
            Win32Native.GetWindowThreadProcessId(source, out var pid);
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string GetWindowTitle(IntPtr source)
    {
        try
        {
            var length = Win32Native.GetWindowTextLengthW(source);
            if (length <= 0)
                return string.Empty;

            var buffer = new StringBuilder(length + 1);
            Win32Native.GetWindowTextW(source, buffer, buffer.Capacity);
            return buffer.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsBrowser(IntPtr source)
    {
        var name = GetProcessName(source);
        return name is not null && BrowserProcesses.Contains(name);
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
