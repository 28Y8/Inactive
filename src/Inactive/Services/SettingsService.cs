using System.IO;
using Inactive.Core.Logging;
using Inactive.Core.Settings;

namespace Inactive.Services;

/// <summary>
/// Holds the current settings and saves every change. Changes come from the UI thread. Any thread
/// may read <see cref="Current"/>, because an instance is never modified once it is published.
/// </summary>
internal sealed class SettingsService
{
    private readonly SettingsStore _store;

    // Saving after a failed read would replace a file that may be fine, PIN included, with defaults.
    private readonly bool _holdsBackSaves;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        Current = store.Load();
        LoadResult = store.LastLoad;
        _holdsBackSaves = LoadResult == SettingsLoadResult.ReadFailed;
        LastSaveFailed = _holdsBackSaves;
    }

    public AppSettings Current { get; private set; }

    /// <summary>How reading the settings went at startup.</summary>
    public SettingsLoadResult LoadResult { get; }

    /// <summary>True when the last change could not be written to disk.</summary>
    public bool LastSaveFailed { get; private set; }

    public event EventHandler? Changed;

    /// <summary>The unlock key or the PIN changed.</summary>
    public event EventHandler? UnlockChanged;

    /// <summary>
    /// Applies a change and saves it. The change stays in effect for this run even if saving fails,
    /// so a full disk costs the setting at the next start and nothing worse.
    /// </summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        var previous = Current;
        var updated = change(previous);
        if (updated == previous)
        {
            return;
        }

        Current = updated;

        if (!_holdsBackSaves)
        {
            Save(updated);
        }

        if (!updated.HasSameUnlock(previous))
        {
            UnlockChanged?.Invoke(this, EventArgs.Empty);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save(AppSettings settings)
    {
        try
        {
            _store.Save(settings);
            LastSaveFailed = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Settings could not be saved.", e);
            LastSaveFailed = true;
        }
    }
}
