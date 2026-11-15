namespace FloatView.Models;

/// <summary>
/// A remembered region stored as fractions of the source window rect so it
/// scales to any window size. Values are 0..1.
/// </summary>
public sealed class SavedRegion
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}

/// <summary>Persisted application settings.</summary>
public sealed class AppSettings
{
    /// <summary>Last region per process name (lower-case, no extension).</summary>
    public Dictionary<string, SavedRegion> Regions { get; set; } = new();

    /// <summary>Last shell window position in DIPs.</summary>
    public double? ShellLeft { get; set; }

    public double? ShellTop { get; set; }

    /// <summary>
    /// When true, a minimized source is restored automatically (without
    /// stealing focus) so the live preview keeps running.
    /// </summary>
    public bool AutoRestoreSource { get; set; }

    /// <summary>Whether the media transport controls are shown in the PiP toolbar.</summary>
    public bool ShowMediaControls { get; set; }

    /// <summary>Arrow presses sent per seek click, tuned to the player's jump size.</summary>
    public int SeekPresses { get; set; } = 1;
}
