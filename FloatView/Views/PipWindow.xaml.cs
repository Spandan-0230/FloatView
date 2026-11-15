using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FloatView.Interop;
using FloatView.Models;
using FloatView.Services;

namespace FloatView.Views;

/// <summary>
/// Always-on-top window that shows a live DWM thumbnail of the selected
/// region. Supports hover toolbar, drag, resize with optional aspect lock.
/// </summary>
public partial class PipWindow : Window
{
    private const double ToolbarHeightDip = 34;

    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszTop = 3;
    private const int WmszTopLeft = 4;
    private const int WmszTopRight = 5;
    private const int WmszBottom = 6;
    private const int WmszBottomLeft = 7;
    private const int WmszBottomRight = 8;

    private readonly SelectionResult _selection;
    private readonly AppSettingsService _settings;
    private readonly ThumbnailService _thumbnail = new();
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _sourceWatch;

    private HwndSource? _hwndSource;
    private IntPtr _hwnd;
    private bool _toolbarExpanded;
    private bool _ratioLocked = true;
    private bool _minimizedShown;
    private bool _syncingOverlay;
    private bool _mediaSettingsReady;
    private IntPtr _minimizeHook;
    private Win32Native.WinEventDelegate? _minimizeHookDelegate;

    /// <summary>True once the DWM thumbnail is attached and the preview is live.</summary>
    public bool IsPreviewReady { get; private set; }

    public PipWindow(SelectionResult selection, AppSettingsService settings)
    {
        InitializeComponent();

        _selection = selection;
        _settings = settings;
        TitleText.Text = selection.SourceTitle;
        Title = selection.SourceTitle + " — FloatView";

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            CollapseIfPointerOutside();
        };

        _sourceWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _sourceWatch.Tick += (_, _) =>
        {
            if (!Win32Native.IsWindow(_selection.SourceHandle))
            {
                Close();
                return;
            }

            SyncMinimizedState(allowAutoRestore: true);
        };

        ApplyMediaControls(_settings.Current.ShowMediaControls);
        _mediaSettingsReady = true;

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        SizeChanged += (_, _) => UpdateThumbnail();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        DwmNative.TryEnableRoundedCorners(_hwnd);
        DwmNative.TryEnableDarkFrame(_hwnd);

        if (!Win32Native.IsWindow(_selection.SourceHandle))
        {
            AppLog.Warn("PiP startup failed: source window is no longer valid");
            return;
        }

        if (!_thumbnail.Attach(_hwnd, _selection.SourceHandle))
        {
            AppLog.Warn("PiP startup failed: thumbnail registration failed");
            return;
        }

        ApplyInitialGeometry();
        UpdateThumbnail();
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        InstallMinimizeHook();
        SyncMinimizedState(allowAutoRestore: true);

        _sourceWatch.Start();
        IsPreviewReady = true;
        Win32Native.GetWindowRect(_hwnd, out var actual);
        AppLog.Info(
            $"PiP started for '{_selection.SourceTitle}' at " +
            $"{actual.Left},{actual.Top} {actual.Width}x{actual.Height}");
    }

    private void InstallMinimizeHook()
    {
        try
        {
            Win32Native.GetWindowThreadProcessId(_selection.SourceHandle, out var sourcePid);
            _minimizeHookDelegate = OnSourceMinimizeEvent;
            _minimizeHook = Win32Native.SetWinEventHook(
                Win32Native.EVENT_SYSTEM_MINIMIZESTART,
                Win32Native.EVENT_SYSTEM_MINIMIZEEND,
                IntPtr.Zero,
                _minimizeHookDelegate,
                sourcePid,
                0,
                Win32Native.WINEVENT_OUTOFCONTEXT);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Could not install minimize hook: " + ex.Message);
        }
    }

    private void OnSourceMinimizeEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint thread,
        uint time)
    {
        if (hwnd != _selection.SourceHandle || Dispatcher.HasShutdownStarted)
            return;

        Dispatcher.BeginInvoke(
            new Action(() => SyncMinimizedState(allowAutoRestore: true)),
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Keeps the preview in sync with the source's minimized state. Windows
    /// stops rendering minimized windows, so a frozen thumbnail is replaced
    /// by the restore overlay (or the source is auto-restored if enabled).
    /// </summary>
    private void SyncMinimizedState(bool allowAutoRestore)
    {
        if (_hwnd == IntPtr.Zero)
            return;

        if (!Win32Native.IsWindow(_selection.SourceHandle))
        {
            Close();
            return;
        }

        if (!Win32Native.IsIconic(_selection.SourceHandle))
        {
            ApplyMinimizedState(false);
            return;
        }

        if (allowAutoRestore && !_minimizedShown && _settings.Current.AutoRestoreSource)
        {
            AppLog.Info("Source minimized; restoring automatically");
            Win32Native.ShowWindow(_selection.SourceHandle, Win32Native.SW_SHOWNOACTIVATE);
            Dispatcher.BeginInvoke(
                new Action(() => SyncMinimizedState(allowAutoRestore: false)),
                DispatcherPriority.Background);
            return;
        }

        ApplyMinimizedState(true);
    }

    private void ApplyMinimizedState(bool minimized)
    {
        if (_minimizedShown == minimized)
            return;

        _minimizedShown = minimized;

        if (minimized)
        {
            AppLog.Info("Source minimized; pausing preview");
            _thumbnail.SetVisible(false);

            _syncingOverlay = true;
            AutoRestoreCheck.IsChecked = _settings.Current.AutoRestoreSource;
            _syncingOverlay = false;

            MinimizeOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            MinimizeOverlay.Visibility = Visibility.Collapsed;
            _thumbnail.SetVisible(true);
            UpdateThumbnail();
            AppLog.Info("Source restored; preview resumed");
        }
    }

    private void ApplyInitialGeometry()
    {
        var ratio = _selection.AspectRatio;
        var work = Win32Native.GetMonitorWorkArea(_selection.SourceHandle);

        var width = (int)Math.Clamp(work.Width * 0.22, 280, 720);
        var height = (int)Math.Round(width / ratio);

        if (height > work.Height * 0.7)
        {
            height = (int)Math.Round(work.Height * 0.7);
            width = Math.Max(220, (int)Math.Round(height * ratio));
        }

        const int margin = 28;
        var x = work.Right - width - margin;
        var y = work.Bottom - height - margin;

        Win32Native.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE);
    }

    private int ToolbarOffsetPx()
    {
        if (!_toolbarExpanded)
            return 0;

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        return (int)Math.Round(ToolbarHeightDip * scale);
    }

    private void UpdateThumbnail()
    {
        if (!_thumbnail.IsAttached || _hwnd == IntPtr.Zero || _minimizedShown)
            return;

        Win32Native.GetClientRect(_hwnd, out var client);
        var offset = ToolbarOffsetPx();
        if (client.Width <= 0 || client.Height - offset <= 0)
            return;

        _thumbnail.Update(
            new Win32Native.RECT(0, offset, client.Width, client.Height),
            _selection.SourceClientRect);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32Native.WM_SIZING:
                if (_ratioLocked)
                {
                    AdjustForAspectRatio(wParam.ToInt32(), lParam);
                    handled = true;
                }
                break;

            case Win32Native.WM_GETMINMAXINFO:
                ApplyMinTrackSize(lParam);
                break;

            case Win32Native.WM_DPICHANGED:
                Dispatcher.BeginInvoke(new Action(UpdateThumbnail), DispatcherPriority.Render);
                break;
        }

        return IntPtr.Zero;
    }

    private void AdjustForAspectRatio(int edge, IntPtr lParam)
    {
        var ratio = _selection.AspectRatio;
        if (ratio <= 0)
            return;

        var rect = Marshal.PtrToStructure<Win32Native.RECT>(lParam);
        var offset = ToolbarOffsetPx();

        var movingLeft = edge is WmszLeft or WmszTopLeft or WmszBottomLeft;
        var movingRight = edge is WmszRight or WmszTopRight or WmszBottomRight;
        var movingTop = edge is WmszTop or WmszTopLeft or WmszTopRight;
        var movingBottom = edge is WmszBottom or WmszBottomLeft or WmszBottomRight;

        if (movingLeft || movingRight)
        {
            var width = Math.Max(160, rect.Width);
            var height = (int)Math.Round(width / ratio) + offset;
            if (movingTop)
                rect.Top = rect.Bottom - height;
            else
                rect.Bottom = rect.Top + height;
        }
        else if (movingTop || movingBottom)
        {
            var height = Math.Max(120, rect.Height);
            var videoHeight = Math.Max(90, height - offset);
            var width = (int)Math.Round(videoHeight * ratio);
            if (movingLeft)
                rect.Left = rect.Right - width;
            else
                rect.Right = rect.Left + width;
        }

        Marshal.StructureToPtr(rect, lParam, false);
    }

    private void ApplyMinTrackSize(IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<Win32Native.MINMAXINFO>(lParam);
        var ratio = _selection.AspectRatio;

        const int minVideo = 140;
        var minWidth = ratio >= 1 ? (int)Math.Ceiling(minVideo * ratio) : minVideo;
        var minHeight = ratio >= 1 ? minVideo : (int)Math.Ceiling(minVideo / ratio);
        var offset = (int)Math.Round(ToolbarHeightDip * VisualTreeHelper.GetDpi(this).DpiScaleY);

        info.ptMinTrackSize = new Win32Native.POINT(
            Math.Max(160, minWidth),
            Math.Max(120, minHeight) + offset);

        Marshal.StructureToPtr(info, lParam, false);
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        ExpandToolbar();
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_toolbarExpanded)
            _collapseTimer.Start();
    }

    private void ExpandToolbar()
    {
        if (_toolbarExpanded || _hwnd == IntPtr.Zero)
            return;

        _toolbarExpanded = true;
        Toolbar.Visibility = Visibility.Visible;
        Toolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));

        var offset = ToolbarOffsetPx();
        Win32Native.GetWindowRect(_hwnd, out var rect);
        Win32Native.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            rect.Left,
            rect.Top - offset,
            rect.Width,
            rect.Height + offset,
            Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE);

        UpdateThumbnail();
    }

    private void CollapseIfPointerOutside()
    {
        if (!_toolbarExpanded || _hwnd == IntPtr.Zero)
            return;

        Win32Native.GetCursorPos(out var cursor);
        Win32Native.GetWindowRect(_hwnd, out var rect);
        if (cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom)
            return;

        _toolbarExpanded = false;
        Toolbar.BeginAnimation(OpacityProperty, null);
        Toolbar.Opacity = 0;
        Toolbar.Visibility = Visibility.Collapsed;

        var offset = (int)Math.Round(ToolbarHeightDip * VisualTreeHelper.GetDpi(this).DpiScaleY);
        Win32Native.GetWindowRect(_hwnd, out var current);
        Win32Native.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            current.Left,
            current.Top + offset,
            current.Width,
            current.Height - offset,
            Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE);

        UpdateThumbnail();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (IsFromButton(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw when the mouse button was already released.
        }
    }

    private static bool IsFromButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ChangeWindow_Click(object sender, RoutedEventArgs e)
    {
        // Show the launcher first: with ShutdownMode.OnLastWindowClose,
        // closing the last PiP before a new window exists would exit the app.
        var shell = new ShellWindow();
        shell.Show();
        shell.Activate();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void LockToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (LockToggle is null || LockGlyph is null)
            return;

        _ratioLocked = LockToggle.IsChecked == true;
        LockGlyph.Text = _ratioLocked ? "\uE72E" : "\uE785";

        if (_ratioLocked)
            SnapToAspectRatio();
    }

    private void ApplyMediaControls(bool show)
    {
        MediaToggle.IsChecked = show;

        MediaControls.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MinWidth = show ? 228 : 160;
        MediaToggle.ToolTip = show ? "Hide media controls" : "Show media controls";
    }

    private void MediaToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (MediaToggle is null || MediaControls is null)
            return;

        var show = MediaToggle.IsChecked == true;
        MediaControls.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MinWidth = show ? 228 : 160;
        MediaToggle.ToolTip = show ? "Hide media controls" : "Show media controls";

        if (_mediaSettingsReady)
            _settings.SetShowMediaControls(show);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        MediaController.TogglePlayPause(_selection.SourceHandle);
    }

    private async void SeekBack_Click(object sender, RoutedEventArgs e) => await SeekAsync(false);

    private async void SeekForward_Click(object sender, RoutedEventArgs e) => await SeekAsync(true);

    private async Task SeekAsync(bool forward)
    {
        try
        {
            await MediaController.SeekAsync(
                _selection.SourceHandle,
                forward,
                _settings.Current.SeekPresses);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Seek failed: " + ex.Message);
        }
    }

    private async void RestoreSource_Click(object sender, RoutedEventArgs e)
    {
        RestoreSource();
        await Task.Delay(180);
        SyncMinimizedState(allowAutoRestore: false);
    }

    private void RestoreSource()
    {
        if (Win32Native.IsWindow(_selection.SourceHandle))
            Win32Native.ShowWindow(_selection.SourceHandle, Win32Native.SW_RESTORE);
    }

    private async void AutoRestore_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingOverlay || AutoRestoreCheck is null)
            return;

        var enabled = AutoRestoreCheck.IsChecked == true;
        _settings.SetAutoRestoreSource(enabled);

        if (enabled)
        {
            RestoreSource();
            await Task.Delay(180);
            SyncMinimizedState(allowAutoRestore: false);
        }
    }

    private void SnapToAspectRatio()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        var offset = (int)Math.Round(ToolbarHeightDip * VisualTreeHelper.GetDpi(this).DpiScaleY);
        Win32Native.GetWindowRect(_hwnd, out var rect);
        var videoHeight = Math.Max(90, rect.Height - offset);
        var width = (int)Math.Round(videoHeight * _selection.AspectRatio);

        Win32Native.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            rect.Left,
            rect.Top,
            Math.Max(160, width),
            rect.Height,
            Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE);

        UpdateThumbnail();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        _sourceWatch.Stop();

        if (_minimizeHook != IntPtr.Zero)
        {
            Win32Native.UnhookWinEvent(_minimizeHook);
            _minimizeHook = IntPtr.Zero;
        }

        _minimizeHookDelegate = null;
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
        _thumbnail.Dispose();
        AppLog.Info("PiP window closed");
    }
}
