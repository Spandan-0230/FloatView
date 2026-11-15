using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FloatView.Interop;
using FloatView.Models;
using FloatView.Services;
using FloatView.ViewModels;

namespace FloatView.Views;

public partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly AppSettingsService _settings;
    private readonly ICollectionView _windowsView;
    private RegionPickerWindow? _picker;
    private bool _startingPip;

    public ShellWindow()
    {
        InitializeComponent();

        _settings = new AppSettingsService();
        _viewModel = new ShellViewModel();
        DataContext = _viewModel;

        _viewModel.StartRequested += OnStartRequested;
        _viewModel.QuitRequested += (_, _) => Close();

        _windowsView = CollectionViewSource.GetDefaultView(_viewModel.Windows);
        _windowsView.Filter = FilterWindow;

        Loaded += (_, _) => RestorePosition();
    }

    private void RestorePosition()
    {
        var area = SystemParameters.WorkArea;

        var left = _settings.Current.ShellLeft ?? area.Left + (area.Width - ActualWidth) / 2;
        var top = _settings.Current.ShellTop ?? area.Top + area.Height * 0.16;

        left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));

        Left = left;
        Top = top;
    }

    private bool FilterWindow(object item)
    {
        if (item is not AppWindow window)
            return false;

        var query = _viewModel.SearchText;
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return window.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
               || window.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Close();

    private void TargetButton_Click(object sender, RoutedEventArgs e)
    {
        if (TargetPopup.IsOpen)
        {
            TargetPopup.IsOpen = false;
            return;
        }

        _viewModel.IsPopupOpen = true;
        TargetPopup.IsOpen = true;
        SearchBox.Text = string.Empty;
        Dispatcher.BeginInvoke(new Action(() => SearchBox.Focus()), DispatcherPriority.Input);
    }

    private void TargetPopup_Closed(object? sender, EventArgs e)
    {
        _viewModel.IsPopupOpen = false;
        _viewModel.SearchText = string.Empty;
        SearchBox.Text = string.Empty;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.SearchText = SearchBox.Text;
        _windowsView.Refresh();
    }

    private void WindowList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TargetPopup.IsOpen)
            return;

        var source = e.OriginalSource as DependencyObject;
        while (source is not null and not ListBoxItem)
            source = VisualTreeHelper.GetParent(source);

        if (source is ListBoxItem { DataContext: AppWindow window })
        {
            WindowList.SelectedItem = window;
            TargetPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void WindowList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && WindowList.SelectedItem is AppWindow && TargetPopup.IsOpen)
        {
            TargetPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && TargetPopup.IsOpen)
        {
            TargetPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && TargetPopup.IsOpen)
        {
            TargetPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private async void OnStartRequested(object? sender, AppWindow target)
    {
        if (_startingPip || _picker is not null)
            return;

        try
        {
            _startingPip = true;
            _viewModel.IsPickerOpen = true;

            TargetPopup.IsOpen = false;
            _viewModel.IsPopupOpen = false;

            AppLog.Info($"Opening region picker for 0x{target.Handle.ToInt64():X} '{target.Title}'");

            // Hide the launcher and raise the chosen window before the overlay
            // appears. The picker is topmost and covers the source exactly, so
            // showing it while an unrelated window is in front makes it look
            // like the picker landed on the wrong app.
            Hide();
            BringToFront(target);

            // Give the source a moment to paint above other windows before the
            // dimmed selection surface covers it.
            await Task.Delay(180);
            if (Dispatcher.HasShutdownStarted)
                return;

            _picker = new RegionPickerWindow(target, _settings);
            _picker.Confirmed += OnPickerConfirmed;
            _picker.Cancelled += OnPickerCancelled;
            _picker.Show();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open region picker", ex);
            ResetPickerState(showShell: true);
        }
        finally
        {
            _startingPip = false;
        }
    }

    /// <summary>
    /// Restores the target if needed and raises it above other normal windows.
    /// The picker itself is topmost, so this keeps the selected app visible
    /// underneath the dimmed selection layer.
    /// </summary>
    private static void BringToFront(AppWindow target)
    {
        try
        {
            if (!Win32Native.IsWindow(target.Handle))
                return;

            if (Win32Native.IsIconic(target.Handle))
                Win32Native.ShowWindow(target.Handle, Win32Native.SW_RESTORE);

            Win32Native.SetWindowPos(
                target.Handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                Win32Native.SWP_NOMOVE | Win32Native.SWP_NOSIZE | Win32Native.SWP_SHOWWINDOW);
            Win32Native.SetForegroundWindow(target.Handle);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Could not raise the target window: " + ex.Message);
        }
    }

    private void OnPickerConfirmed(object? sender, SelectionResult selection)
    {
        try
        {
            AppLog.Info(
                $"Picker confirmed: source=0x{selection.SourceHandle.ToInt64():X} " +
                $"rect={selection.SourceClientRect.Left},{selection.SourceClientRect.Top}," +
                $"{selection.SourceClientRect.Width}x{selection.SourceClientRect.Height}");

            var pip = new PipWindow(selection, _settings);
            pip.Show();
            if (!pip.IsPreviewReady)
            {
                pip.Close();
                throw new InvalidOperationException(
                    "The selected window is no longer available, or its preview could not be created.");
            }

            _viewModel.IsPickerOpen = false;
            Close();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open PiP window", ex);
            MessageBox.Show(
                "Could not start Picture-in-Picture for that window.\n\n" +
                "It may have been closed or minimized.",
                "FloatView",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ResetPickerState(showShell: true);
        }
    }

    private void OnPickerCancelled(object? sender, EventArgs e) => ResetPickerState(showShell: true);

    private void ResetPickerState(bool showShell)
    {
        if (_picker is not null)
        {
            _picker.Confirmed -= OnPickerConfirmed;
            _picker.Cancelled -= OnPickerCancelled;
            _picker = null;
        }

        _viewModel.IsPickerOpen = false;

        if (showShell && IsVisible == false)
        {
            Show();
            Activate();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        AppLog.Info("Shell window closing");
        _settings.SetShellPosition(Left, Top);

        if (_picker is not null)
        {
            _picker.Confirmed -= OnPickerConfirmed;
            _picker.Cancelled -= OnPickerCancelled;
            _picker.Close();
            _picker = null;
        }

        _viewModel.Dispose();
    }
}
