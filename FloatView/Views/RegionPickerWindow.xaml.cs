using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FloatView.Interop;
using FloatView.Models;
using FloatView.Services;

namespace FloatView.Views;

/// <summary>
/// Transparent overlay positioned exactly over the target window. Lets the
/// user drag out the region to mirror.
/// </summary>
public partial class RegionPickerWindow : Window
{
    private readonly AppWindow _target;
    private readonly AppSettingsService _settings;

    private IntPtr _hwnd;
    private Win32Native.RECT _windowRect;
    private Win32Native.POINT _clientOrigin;
    private Win32Native.RECT _clientSize;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private Rect _clientBoundsDip;
    private bool _initialized;
    private bool _finished;

    public RegionPickerWindow(AppWindow target, AppSettingsService settings)
    {
        InitializeComponent();

        _target = target;
        _settings = settings;
        TargetTitleText.Text = target.Title;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    public event EventHandler<SelectionResult>? Confirmed;

    public event EventHandler? Cancelled;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        if (!Win32Native.IsWindow(_target.Handle))
        {
            AppLog.Warn("Picker cancelling: target window is no longer valid");
            Dispatcher.BeginInvoke(new Action(Cancel));
            return;
        }

        if (Win32Native.IsIconic(_target.Handle))
            Win32Native.ShowWindow(_target.Handle, Win32Native.SW_SHOWNOACTIVATE);

        // Raise the source without activating it. Activating a browser here can
        // move its internal focus to the address bar, which would break the
        // focus-free media keys later. The picker itself is activated below.
        Win32Native.SetWindowPos(
            _target.Handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            Win32Native.SWP_NOMOVE | Win32Native.SWP_NOSIZE | Win32Native.SWP_NOACTIVATE);

        if (!Win32Native.GetWindowRect(_target.Handle, out _windowRect) ||
            _windowRect.Width <= 0 ||
            _windowRect.Height <= 0)
        {
            AppLog.Warn($"Picker cancelling: invalid target rect {_windowRect.Width}x{_windowRect.Height}");
            Dispatcher.BeginInvoke(new Action(Cancel));
            return;
        }

        var clientOk = Win32Native.GetClientRect(_target.Handle, out _clientSize);
        var origin = new Win32Native.POINT(0, 0);
        var originOk = Win32Native.ClientToScreen(_target.Handle, ref origin);
        _clientOrigin = origin;

        if (!clientOk || !originOk || _clientSize.Width <= 0 || _clientSize.Height <= 0)
        {
            AppLog.Warn("Picker cancelling: target has no usable client area");
            Dispatcher.BeginInvoke(new Action(Cancel));
            return;
        }

        // Position and show the overlay only after all target geometry is
        // captured: SWP_SHOWWINDOW raises Loaded synchronously, and Loaded
        // needs the client rect to size the initial selection.
        Win32Native.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            _windowRect.Left,
            _windowRect.Top,
            _windowRect.Width,
            _windowRect.Height,
            Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE | Win32Native.SWP_SHOWWINDOW);

        Activate();
        Win32Native.SetForegroundWindow(_hwnd);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_finished || _hwnd == IntPtr.Zero || _windowRect.Width <= 0)
        {
            AppLog.Warn($"Picker Loaded skipped (finished={_finished}, hwnd=0x{_hwnd.ToInt64():X}, rectWidth={_windowRect.Width})");
            return;
        }

        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0)
        {
            AppLog.Warn($"Picker Loaded skipped: zero layout ({Root.ActualWidth}x{Root.ActualHeight})");
            return;
        }

        _scaleX = _windowRect.Width / Root.ActualWidth;
        _scaleY = _windowRect.Height / Root.ActualHeight;

        _clientBoundsDip = new Rect(
            (_clientOrigin.X - _windowRect.Left) / _scaleX,
            (_clientOrigin.Y - _windowRect.Top) / _scaleY,
            _clientSize.Width / _scaleX,
            _clientSize.Height / _scaleY);

        Selector.Initialize(_clientBoundsDip, BuildInitialSelection());
        _initialized = true;
        Selector.Focus();

        AppLog.Info(
            $"Picker ready (layout={Root.ActualWidth}x{Root.ActualHeight}, " +
            $"window={_windowRect.Width}x{_windowRect.Height}, client={_clientSize.Width}x{_clientSize.Height}, " +
            $"selection={Selector.Selection.Left:F1},{Selector.Selection.Top:F1} " +
            $"{Selector.Selection.Width:F1}x{Selector.Selection.Height:F1})");
    }

    private Rect BuildInitialSelection()
    {
        var saved = _settings.GetRegion(_target.ProcessName);
        if (saved is not null && saved.Width > 0.01 && saved.Height > 0.01)
        {
            return new Rect(
                _clientBoundsDip.X + saved.X * _clientBoundsDip.Width,
                _clientBoundsDip.Y + saved.Y * _clientBoundsDip.Height,
                Math.Max(0.05, saved.Width) * _clientBoundsDip.Width,
                Math.Max(0.05, saved.Height) * _clientBoundsDip.Height);
        }

        return new Rect(
            _clientBoundsDip.X + _clientBoundsDip.Width * 0.2,
            _clientBoundsDip.Y + _clientBoundsDip.Height * 0.2,
            Math.Max(40, _clientBoundsDip.Width * 0.6),
            Math.Max(40, _clientBoundsDip.Height * 0.6));
    }

    private void Confirm()
    {
        if (_finished || !_initialized)
        {
            AppLog.Warn($"Picker Confirm ignored (finished={_finished}, initialized={_initialized})");
            return;
        }

        var selection = Selector.Selection;
        if (selection.Width < Selector.MinSize || selection.Height < Selector.MinSize)
        {
            AppLog.Warn($"Picker Confirm ignored: selection too small ({selection.Width:F1}x{selection.Height:F1})");
            return;
        }

        var insetLeft = (int)Math.Round(_clientBoundsDip.X * _scaleX);
        var insetTop = (int)Math.Round(_clientBoundsDip.Y * _scaleY);

        var rect = new Win32Native.RECT(
            (int)Math.Round(selection.Left * _scaleX) - insetLeft,
            (int)Math.Round(selection.Top * _scaleY) - insetTop,
            (int)Math.Round(selection.Right * _scaleX) - insetLeft,
            (int)Math.Round(selection.Bottom * _scaleY) - insetTop);

        if (_clientBoundsDip.Width > 0 && _clientBoundsDip.Height > 0)
        {
            _settings.SetRegion(_target.ProcessName, new Rect(
                (selection.Left - _clientBoundsDip.X) / _clientBoundsDip.Width,
                (selection.Top - _clientBoundsDip.Y) / _clientBoundsDip.Height,
                selection.Width / _clientBoundsDip.Width,
                selection.Height / _clientBoundsDip.Height));
        }

        _finished = true;
        Confirmed?.Invoke(this, new SelectionResult
        {
            SourceHandle = _target.Handle,
            SourceTitle = _target.Title,
            SourceClientRect = rect,
        });
        Close();
    }

    private void Cancel()
    {
        if (_finished)
            return;

        _finished = true;
        Cancelled?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_finished)
            return;

        _finished = true;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
