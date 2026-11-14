using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatView.Interop;

namespace FloatView.Services;

/// <summary>
/// Extracts small icons from windows for display in the target list.
/// Icons returned by WM_GETICON / GetClassLongPtr are owned by the source
/// window, so they are converted but never destroyed here.
/// </summary>
public sealed class IconService
{
    private readonly Dictionary<IntPtr, ImageSource?> _cache = new();

    public ImageSource? TryGetIcon(IntPtr hWnd)
    {
        if (_cache.TryGetValue(hWnd, out var cached))
            return cached;

        ImageSource? icon = null;

        try
        {
            var hIcon = QueryWindowIcon(hWnd, Win32Native.ICON_BIG);
            if (hIcon == IntPtr.Zero)
                hIcon = QueryWindowIcon(hWnd, Win32Native.ICON_SMALL2);
            if (hIcon == IntPtr.Zero)
                hIcon = QueryWindowIcon(hWnd, Win32Native.ICON_SMALL);
            if (hIcon == IntPtr.Zero)
                hIcon = Win32Native.GetClassLongPtr(hWnd, Win32Native.GCLP_HICON);
            if (hIcon == IntPtr.Zero)
                hIcon = Win32Native.GetClassLongPtr(hWnd, Win32Native.GCLP_HICONSM);

            if (hIcon != IntPtr.Zero)
            {
                icon = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
            }
        }
        catch
        {
            icon = null;
        }

        if (_cache.Count > 256)
            _cache.Clear();

        _cache[hWnd] = icon;
        return icon;
    }

    /// <summary>
    /// Requests a window icon without risking an indefinite block on an
    /// unresponsive application.
    /// </summary>
    private static IntPtr QueryWindowIcon(IntPtr hWnd, int iconType)
    {
        var ok = Win32Native.SendMessageTimeout(
            hWnd,
            Win32Native.WM_GETICON,
            (IntPtr)iconType,
            IntPtr.Zero,
            Win32Native.SMTO_ABORTIFHUNG,
            120,
            out var result);

        return ok == IntPtr.Zero ? IntPtr.Zero : result;
    }
}
