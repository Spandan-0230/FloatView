using System.IO;
using System.Text.Json;
using System.Windows;
using FloatView.Models;

namespace FloatView.Services;

/// <summary>
/// Loads and saves FloatView settings as JSON under %LocalAppData%\FloatView.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public AppSettingsService()
    {
        _path = Path.Combine(AppLog.FolderPath, "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; }

    public SavedRegion? GetRegion(string processName)
    {
        return Current.Regions.TryGetValue(Normalize(processName), out var region) ? region : null;
    }

    public void SetRegion(string processName, Rect fractions)
    {
        Current.Regions[Normalize(processName)] = new SavedRegion
        {
            X = fractions.X,
            Y = fractions.Y,
            Width = fractions.Width,
            Height = fractions.Height,
        };
        Save();
    }

    public void SetShellPosition(double left, double top)
    {
        Current.ShellLeft = left;
        Current.ShellTop = top;
        Save();
    }

    public void SetAutoRestoreSource(bool value)
    {
        Current.AutoRestoreSource = value;
        Save();
    }

    public void SetShowMediaControls(bool value)
    {
        Current.ShowMediaControls = value;
        Save();
    }

    private static string Normalize(string processName) => processName.ToLowerInvariant();

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                {
                    settings.Regions ??= new Dictionary<string, SavedRegion>();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to load settings, starting fresh", ex);
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppLog.FolderPath);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to save settings", ex);
        }
    }
}
