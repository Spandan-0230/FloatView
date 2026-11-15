using System.Linq;
using System.Windows;
using System.Windows.Interop;
using FloatView.Interop;
using FloatView.Views;

namespace FloatView.Services;

/// <summary>
/// Reopens the launcher window. Used whenever the Picture-in-Picture window
/// closes so the app falls back to its front screen instead of exiting.
/// </summary>
public static class ShellLauncher
{
    public static void Show()
    {
        try
        {
            var existing = Application.Current?.Windows
                .OfType<ShellWindow>()
                .FirstOrDefault();

            if (existing is not null)
            {
                if (existing.WindowState == WindowState.Minimized)
                    existing.WindowState = WindowState.Normal;

                if (!existing.IsVisible)
                    existing.Show();

                existing.Activate();
                BringToFront(existing);
                AppLog.Info("Launcher reopened (existing window)");
                return;
            }

            var shell = new ShellWindow();
            shell.Show();
            shell.Activate();
            BringToFront(shell);
            AppLog.Info("Launcher reopened");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Could not reopen the launcher: " + ex.Message);
        }
    }

    private static void BringToFront(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            Win32Native.SetForegroundWindow(hwnd);
    }
}
