using System.Windows.Media;

namespace FloatView.Models;

/// <summary>Snapshot of a top-level window that can be mirrored.</summary>
public sealed class AppWindow
{
    public AppWindow(IntPtr handle, string title, string processName, ImageSource? icon)
    {
        Handle = handle;
        Title = title;
        ProcessName = processName;
        Icon = icon;
    }

    public IntPtr Handle { get; }

    public string Title { get; }

    public string ProcessName { get; }

    public ImageSource? Icon { get; }

    public override string ToString() => Title;
}
