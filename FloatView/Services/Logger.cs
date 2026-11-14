using System.IO;
using System.Text;

namespace FloatView.Services;

/// <summary>
/// Minimal, dependency-free file logger. Keeps a single rolling log file
/// under %LocalAppData%\FloatView.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string FolderPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatView");

    public static string LogPath { get; } = Path.Combine(FolderPath, "floatview.log");

    /// <summary>Rotates the log if it grew too large, then ensures the folder exists.</summary>
    public static void StartSession()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);

            var file = new FileInfo(LogPath);
            if (file.Exists && file.Length > 250 * 1024)
            {
                var old = LogPath + ".old";
                if (File.Exists(old))
                    File.Delete(old);
                File.Move(LogPath, old);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message) => Write("WARN ", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);

            if (exception is not null)
                line.Append(Environment.NewLine).Append(exception);

            lock (Gate)
            {
                Directory.CreateDirectory(FolderPath);
                File.AppendAllText(LogPath, line.Append(Environment.NewLine).ToString());
            }
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}
