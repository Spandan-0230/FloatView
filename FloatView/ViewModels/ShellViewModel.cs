using System.Collections.ObjectModel;
using FloatView.Interop;
using FloatView.Models;
using FloatView.Services;

namespace FloatView.ViewModels;

/// <summary>Backs the launcher card: live window list, selection, and actions.</summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly WindowListService _windowListService;
    private AppWindow? _selectedWindow;
    private string _searchText = string.Empty;
    private bool _isPopupOpen;
    private bool _isPickerOpen;
    private bool _disposed;

    public ShellViewModel()
    {
        _windowListService = new WindowListService();
        _windowListService.WindowsChanged += OnWindowsChanged;
        _windowListService.ForegroundWindowChanged += OnForegroundWindowChanged;
        _windowListService.Start();

        StartCommand = new RelayCommand(() =>
        {
            if (SelectedWindow is not null)
                StartRequested?.Invoke(this, SelectedWindow);
        }, () => SelectedWindow is not null);

        QuitCommand = new RelayCommand(() => QuitRequested?.Invoke(this, EventArgs.Empty));

        RefreshWindows();
    }

    public ObservableCollection<AppWindow> Windows { get; } = new();

    public RelayCommand StartCommand { get; }

    public RelayCommand QuitCommand { get; }

    public event EventHandler<AppWindow>? StartRequested;

    public event EventHandler? QuitRequested;

    public AppWindow? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            if (SetProperty(ref _selectedWindow, value))
                StartCommand.RaiseCanExecuteChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public bool IsPopupOpen
    {
        get => _isPopupOpen;
        set => SetProperty(ref _isPopupOpen, value);
    }

    public bool IsPickerOpen
    {
        get => _isPickerOpen;
        set => SetProperty(ref _isPickerOpen, value);
    }

    private void OnWindowsChanged(object? sender, EventArgs e) => RefreshWindows();

    private void OnForegroundWindowChanged(object? sender, IntPtr handle)
    {
        if (IsPopupOpen || IsPickerOpen)
            return;

        var match = Windows.FirstOrDefault(w => w.Handle == handle);
        if (match is not null)
            SelectedWindow = match;
    }

    private void RefreshWindows()
    {
        var selectedHandle = SelectedWindow?.Handle;
        var fresh = _windowListService.Snapshot();

        Windows.Clear();
        foreach (var window in fresh)
            Windows.Add(window);

        AppWindow? restored = null;
        if (selectedHandle is not null)
            restored = Windows.FirstOrDefault(w => w.Handle == selectedHandle);

        if (restored is null)
        {
            var foreground = Win32Native.GetForegroundWindow();
            restored = Windows.FirstOrDefault(w => w.Handle == foreground) ?? Windows.FirstOrDefault();
        }

        SelectedWindow = restored;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _windowListService.WindowsChanged -= OnWindowsChanged;
        _windowListService.ForegroundWindowChanged -= OnForegroundWindowChanged;
        _windowListService.Dispose();
    }
}
