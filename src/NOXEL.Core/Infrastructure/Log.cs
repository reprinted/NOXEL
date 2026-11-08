using System.Text;

namespace Noxel.Core.Infrastructure;

/// <summary>
/// Minimal local file logger. Diagnostic logging is opt-in (Settings > Diagnostics). Crash reports are
/// always written locally so a failed start can be explained. No personal data, no network.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    public static bool Enabled { get; set; }
    private const long MaxBytes = 1024 * 1024;

    public static void Info(string message) { if (Enabled) Write("INFO", message); }
    public static void Warn(string message) { if (Enabled) Write("WARN", message); }
    public static void Error(string message, Exception? ex = null) { if (Enabled) Write("ERROR", ex is null ? message : $"{message}: {ex}"); }

    public static void Crash(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            File.AppendAllText(Path.Combine(AppPaths.Logs, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* never throw from the crash path */ }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var path = Path.Combine(AppPaths.Logs, "noxel.log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Move(path, Path.Combine(AppPaths.Logs, "noxel.old.log"), overwrite: true);
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* logging must never break the app */ }
    }

    public static void DeleteAll()
    {
        lock (Gate)
        {
            if (!Directory.Exists(AppPaths.Logs)) return;
            foreach (var f in Directory.GetFiles(AppPaths.Logs, "*.log"))
                try { File.Delete(f); } catch { }
        }
    }
}
