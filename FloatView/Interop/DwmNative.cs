using System.Runtime.InteropServices;

namespace FloatView.Interop;

/// <summary>
/// Desktop Window Manager interop: live thumbnail mirroring and window
/// appearance attributes.
/// </summary>
public static class DwmNative
{
    // DwmGetWindowAttribute attributes
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // DWM window corner preference values (Windows 11)
    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    // DWM thumbnail property flags
    public const int DWM_TNP_RECTDESTINATION = 0x00000001;
    public const int DWM_TNP_RECTSOURCE = 0x00000002;
    public const int DWM_TNP_OPACITY = 0x00000004;
    public const int DWM_TNP_VISIBLE = 0x00000008;
    public const int DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public Win32Native.RECT rcDestination;
        public Win32Native.RECT rcSource;
        public byte opacity;
        public int fVisible;
        public int fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUpdateThumbnailProperties(
        IntPtr thumbnail,
        ref DWM_THUMBNAIL_PROPERTIES properties);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        out int value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        out Win32Native.RECT value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        ref int value,
        int valueSize);

    public static bool IsCloaked(IntPtr hWnd)
    {
        var hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    public static bool TryGetVisibleFrame(IntPtr hWnd, out Win32Native.RECT frame)
    {
        var hr = DwmGetWindowAttribute(
            hWnd,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            out frame,
            Marshal.SizeOf<Win32Native.RECT>());
        return hr == 0;
    }

    /// <summary>Applies rounded corners on Windows 11; harmless no-op on Windows 10.</summary>
    public static void TryEnableRoundedCorners(IntPtr hWnd)
    {
        try
        {
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch
        {
            // Older Windows versions do not support this attribute.
        }
    }

    /// <summary>Hints dark title-bar rendering for the window frame.</summary>
    public static void TryEnableDarkFrame(IntPtr hWnd)
    {
        try
        {
            int enabled = 1;
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        }
        catch
        {
            // Older Windows versions do not support this attribute.
        }
    }
}
