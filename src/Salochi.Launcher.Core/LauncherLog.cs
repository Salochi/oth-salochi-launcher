namespace Salochi.Launcher.Core;

/// <summary>
/// %LOCALAPPDATA%\Salochi\Launcher\launcher.log: what the launcher did and every error in full, for bug reports.
/// Kept under 1 MB (the previous file becomes launcher.log.1).
/// </summary>
public static class LauncherLog
{
    private const long MaxBytes = 1 << 20;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(LauncherSettings.DataDir, "launcher.log");

    public static void Info(string message) => Write("info", message);

    public static void Error(string what, Exception e) => Write("error", what + ": " + e);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LauncherSettings.DataDir);
                var f = new FileInfo(FilePath);
                if (f.Exists && f.Length > MaxBytes) File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception) { /* logging must never break the launcher */ }
    }

    /// <summary>The exception's message, or its type when the message is empty.</summary>
    public static string Describe(Exception e) => string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message.TrimEnd('.');
}
