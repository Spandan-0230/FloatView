using FloatView.Interop;

namespace FloatView.Services;

/// <summary>
/// Wraps a DWM live-thumbnail registration between the FloatView window and
/// a source window.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    private IntPtr _thumbnail;

    public bool IsAttached => _thumbnail != IntPtr.Zero;

    /// <summary>Registers a live thumbnail of <paramref name="source"/> inside <paramref name="destination"/>.</summary>
    public bool Attach(IntPtr destination, IntPtr source)
    {
        Detach();

        try
        {
            var hr = DwmNative.DwmRegisterThumbnail(destination, source, out _thumbnail);
            if (hr != 0)
            {
                AppLog.Warn($"DwmRegisterThumbnail failed: 0x{hr:X8}");
                _thumbnail = IntPtr.Zero;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("DwmRegisterThumbnail threw", ex);
            _thumbnail = IntPtr.Zero;
            return false;
        }
    }

    /// <summary>
    /// Updates the visible region.
    /// </summary>
    /// <param name="destination">Rectangle in the destination window's client space (physical px).</param>
    /// <param name="source">Rectangle in the source window's client space (physical px).</param>
    public void Update(Win32Native.RECT destination, Win32Native.RECT source)
    {
        if (_thumbnail == IntPtr.Zero)
            return;

        var props = new DwmNative.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DwmNative.DWM_TNP_VISIBLE
                      | DwmNative.DWM_TNP_RECTDESTINATION
                      | DwmNative.DWM_TNP_RECTSOURCE
                      | DwmNative.DWM_TNP_OPACITY
                      | DwmNative.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = destination,
            rcSource = source,
            opacity = 255,
            fVisible = 1,
            fSourceClientAreaOnly = 1,
        };

        var hr = DwmNative.DwmUpdateThumbnailProperties(_thumbnail, ref props);
        if (hr != 0)
            AppLog.Warn($"DwmUpdateThumbnailProperties failed: 0x{hr:X8}");
    }

    public void Detach()
    {
        if (_thumbnail == IntPtr.Zero)
            return;

        try
        {
            DwmNative.DwmUnregisterThumbnail(_thumbnail);
        }
        catch (Exception ex)
        {
            AppLog.Warn("DwmUnregisterThumbnail threw: " + ex.Message);
        }

        _thumbnail = IntPtr.Zero;
    }

    public void Dispose() => Detach();
}
