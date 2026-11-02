using System.Globalization;
using System.Text;

namespace Inactive.Core.Logging;

/// <summary>
/// A small rolling file log under %APPDATA%\Inactive\logs. Logging never throws. Nothing here may
/// be called from a hook callback, and callers must never pass key values or PIN digits.
/// </summary>
public static class AppLog
{
    public const long MaxFileBytes = 1024 * 1024;

    private const string FileName = "inactive.log";
    private const string RolledFileName = "inactive.1.log";

    private static readonly Lock Gate = new();
    private static string _directory = DefaultDirectory;

    /// <summary>%APPDATA%\Inactive\logs.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Inactive", "logs");

    /// <summary>The folder that holds the log files.</summary>
    public static string LogDirectory
    {
        get
        {
            lock (Gate)
            {
                return _directory;
            }
        }
    }

    /// <summary>Sends the log to another folder.</summary>
    public static void Configure(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        lock (Gate)
        {
            _directory = directory;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
        var bytes = Encoding.UTF8.GetBytes(line);

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, FileName);

                var existing = new FileInfo(path);
                if (existing.Exists && existing.Length + bytes.Length > MaxFileBytes)
                {
                    File.Move(path, Path.Combine(_directory, RolledFileName), overwrite: true);
                }

                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // A log that cannot be written must never take the app down with it.
            }
        }
    }
}
