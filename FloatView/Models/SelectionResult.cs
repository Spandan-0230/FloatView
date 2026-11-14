using FloatView.Interop;

namespace FloatView.Models;

/// <summary>Result of the region selection step.</summary>
public sealed class SelectionResult
{
    /// <summary>Handle of the window being mirrored.</summary>
    public required IntPtr SourceHandle { get; init; }

    public required string SourceTitle { get; init; }

    /// <summary>Selected rectangle in the source window's client space (physical pixels).</summary>
    public required Win32Native.RECT SourceClientRect { get; init; }

    public double AspectRatio =>
        SourceClientRect.Height <= 0 ? 1.0 : SourceClientRect.Width / (double)SourceClientRect.Height;
}
