using System.Text.Json;
using Inactive.Core.Logging;

namespace Inactive.Core.Settings;

/// <summary>How the last <see cref="SettingsStore.Load"/> went.</summary>
public enum SettingsLoadResult
{
    /// <summary>The file was read, or there was none yet.</summary>
    Loaded,

    /// <summary>
    /// The file exists but could not be read. Defaults are in use and the file may be fine, so it
    /// must not be saved over.
    /// </summary>
    ReadFailed,

    /// <summary>The file held no valid settings. It was kept as a backup and defaults are in use.</summary>
    Corrupt,
}

/// <summary>Loads and saves settings.json. Saving replaces the file atomically.</summary>
public sealed class SettingsStore
{
    private const string FileName = "settings.json";
    private const int FileAttempts = 5;

    /// <summary>%APPDATA%\Inactive, which survives updates because it sits outside the install folder.</summary>
    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Inactive");

    public SettingsStore()
        : this(DefaultDirectory)
    {
    }

    public SettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        FilePath = Path.Combine(directory, FileName);
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + ".bak";

    /// <summary>How the last <see cref="Load"/> went.</summary>
    public SettingsLoadResult LastLoad { get; private set; }

    /// <summary>
    /// Reads the settings. A missing file gives defaults. A corrupt file gives defaults and is kept
    /// as <see cref="BackupPath"/>. A file that stays unreadable gives defaults and is left alone.
    /// <see cref="LastLoad"/> tells these apart. Never throws for a bad file.
    /// </summary>
    public AppSettings Load()
    {
        LastLoad = SettingsLoadResult.Loaded;

        byte[] content;
        try
        {
            content = ReadWithRetry();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Settings could not be read, using defaults.", e);
            LastLoad = SettingsLoadResult.ReadFailed;
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize(WithoutByteOrderMark(content), AppSettingsJsonContext.Default.AppSettings);
            if (settings is not null)
            {
                // A hand edit can put null here, and the deserializer lets it past the annotation.
                settings.CustomMessage ??= string.Empty;
                return settings;
            }
        }
        catch (JsonException)
        {
            // Handled below, together with a file that holds a bare null.
        }

        LastLoad = SettingsLoadResult.Corrupt;
        KeepCorruptFile();
        return new AppSettings();
    }

    /// <summary>Writes the settings to a temporary file, then swaps it in.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, AppSettingsJsonContext.Default.AppSettings);
                stream.Flush(flushToDisk: true);
            }

            ReplaceWithRetry(temporaryPath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    // Editors and PowerShell can save UTF-8 with a byte order mark, which the JSON reader rejects.
    private static ReadOnlySpan<byte> WithoutByteOrderMark(ReadOnlySpan<byte> content) =>
        content.StartsWith<byte>([0xEF, 0xBB, 0xBF]) ? content[3..] : content;

    // Antivirus and indexers can hold the file open for a moment.
    private byte[] ReadWithRetry()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllBytes(FilePath);
            }
            catch (Exception e) when (IsWorthRetrying(e) && attempt < FileAttempts)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static bool IsWorthRetrying(Exception e) =>
        e is IOException or UnauthorizedAccessException
        && e is not (FileNotFoundException or DirectoryNotFoundException);

    private void ReplaceWithRetry(string temporaryPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, FilePath, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < FileAttempts)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private void KeepCorruptFile()
    {
        try
        {
            File.Move(FilePath, BackupPath, overwrite: true);
            AppLog.Warn("Settings file was corrupt. It was kept as settings.json.bak and defaults are in use.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Corrupt settings file could not be kept as a backup.", e);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("A temporary settings file could not be removed.");
        }
    }
}
