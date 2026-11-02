using System.Text.Json;
using Inactive.Core.Input;
using Inactive.Core.Security;
using Inactive.Core.Settings;
using Inactive.Tests.TestSupport;

namespace Inactive.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly SettingsStore _store;

    public SettingsStoreTests() => _store = new SettingsStore(_directory.Path);

    public void Dispose() => _directory.Dispose();

    private void WriteRaw(string json) => File.WriteAllText(_store.FilePath, json);

    [Fact]
    public void A_byte_order_mark_is_not_treated_as_corruption()
    {
        File.WriteAllBytes(_store.FilePath, [0xEF, 0xBB, 0xBF, .. "{ \"CustomMessage\": \"Back soon\" }"u8]);

        var settings = _store.Load();

        Assert.Equal("Back soon", settings.CustomMessage);
        Assert.Equal(SettingsLoadResult.Loaded, _store.LastLoad);
        Assert.False(File.Exists(_store.BackupPath));
    }

    [Fact]
    public void Default_path_is_under_appdata()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Inactive", "settings.json");

        Assert.Equal(expected, new SettingsStore().FilePath);
    }

    [Fact]
    public void Defaults_match_the_spec()
    {
        var settings = new AppSettings();

        Assert.Equal(0xA1, settings.UnlockKey);
        Assert.Null(settings.PinHash);
        Assert.Null(settings.PinSalt);
        Assert.Null(settings.PinIterations);
        Assert.Null(settings.PinLength);
        Assert.Null(settings.Pin);
        Assert.Equal(HotkeyGesture.Parse("Ctrl+Alt+L"), settings.LockHotkey);
        Assert.Equal(ScreenStyle.Dimmed, settings.ScreenStyle);
        Assert.True(settings.ShowClock);
        Assert.Equal(string.Empty, settings.CustomMessage);
        Assert.False(settings.ShowUnlockHint);
        Assert.False(settings.AllowDisplayOff);
        Assert.False(settings.StartWithWindows);
        Assert.True(settings.AutoUpdate);
        Assert.False(settings.HasSeenFirstLockHint);
        Assert.False(settings.HasSeenTrayHint);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        Assert.Equal(new AppSettings(), _store.Load());
        Assert.False(File.Exists(_store.BackupPath));
    }

    [Fact]
    public void Missing_folder_gives_defaults()
    {
        var store = new SettingsStore(Path.Combine(_directory.Path, "nowhere"));

        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void Save_creates_the_folder()
    {
        var store = new SettingsStore(Path.Combine(_directory.Path, "new", "folder"));

        store.Save(new AppSettings());

        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Every_field_survives_a_round_trip()
    {
        var pin = PinHasher.Create("123456", 1_000);
        var settings = new AppSettings
        {
            UnlockKey = VirtualKeys.Pause,
            LockHotkey = HotkeyGesture.Parse("Ctrl+Shift+F9"),
            ScreenStyle = ScreenStyle.Covered,
            ShowClock = false,
            CustomMessage = "Back in 5 minutes",
            ShowUnlockHint = true,
            AllowDisplayOff = true,
            StartWithWindows = true,
            AutoUpdate = false,
            HasSeenFirstLockHint = true,
            HasSeenTrayHint = true,
        }.WithPin(pin);

        _store.Save(settings);

        var loaded = _store.Load();
        Assert.Equal(settings, loaded);
        Assert.Equal(pin, loaded.Pin);
    }

    [Fact]
    public void A_turned_off_hotkey_stays_off()
    {
        _store.Save(new AppSettings { LockHotkey = null });

        Assert.Null(_store.Load().LockHotkey);
    }

    [Fact]
    public void A_removed_pin_stays_removed()
    {
        var settings = new AppSettings().WithPin(PinHasher.Create("1234", 1_000));

        _store.Save(settings.WithPin(null));

        var loaded = _store.Load();
        Assert.Null(loaded.Pin);
        Assert.Null(loaded.PinHash);
        Assert.Null(loaded.PinLength);
    }

    [Fact]
    public void Partial_pin_data_reads_as_no_pin()
    {
        var settings = new AppSettings().WithPin(PinHasher.Create("1234", 1_000)) with { PinSalt = null };

        Assert.Null(settings.Pin);
    }

    [Fact]
    public void File_uses_the_documented_field_names()
    {
        _store.Save(new AppSettings());

        using var document = JsonDocument.Parse(File.ReadAllText(_store.FilePath));
        var root = document.RootElement;

        Assert.Equal(0xA1, root.GetProperty("UnlockKey").GetInt32());
        Assert.Equal("Ctrl+Alt+L", root.GetProperty("LockHotkey").GetString());
        Assert.Equal("Dimmed", root.GetProperty("ScreenStyle").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("PinHash").ValueKind);
        Assert.False(root.TryGetProperty("Pin", out _));
    }

    [Fact]
    public void Saving_replaces_the_previous_file_and_leaves_no_temporary_file()
    {
        _store.Save(new AppSettings { CustomMessage = "first" });
        _store.Save(new AppSettings { CustomMessage = "second" });

        Assert.Equal("second", _store.Load().CustomMessage);
        Assert.Equal(["settings.json"], Directory.GetFiles(_directory.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void A_failed_save_keeps_the_old_file_and_cleans_up()
    {
        _store.Save(new AppSettings { CustomMessage = "kept" });

        // An open handle without delete sharing makes the final swap fail.
        using (new FileStream(_store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => _store.Save(new AppSettings { CustomMessage = "lost" }));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }

        Assert.Equal("kept", _store.Load().CustomMessage);
        Assert.Equal(["settings.json"], Directory.GetFiles(_directory.Path).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ this is not json")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"UnlockKey\": \"not a number\" }")]
    public void Corrupt_file_gives_defaults_and_is_kept_as_bak(string content)
    {
        WriteRaw(content);

        var settings = _store.Load();

        Assert.Equal(new AppSettings(), settings);
        Assert.False(File.Exists(_store.FilePath));
        Assert.Equal(content, File.ReadAllText(_store.BackupPath));
    }

    [Fact]
    public void A_new_corrupt_file_replaces_the_old_backup()
    {
        File.WriteAllText(_store.BackupPath, "older");
        WriteRaw("newer");

        _store.Load();

        Assert.Equal("newer", File.ReadAllText(_store.BackupPath));
    }

    [Fact]
    public void Saving_after_corruption_works()
    {
        WriteRaw("broken");
        var settings = _store.Load() with { CustomMessage = "recovered" };

        _store.Save(settings);

        Assert.Equal("recovered", _store.Load().CustomMessage);
    }

    [Fact]
    public void Unknown_fields_are_tolerated()
    {
        WriteRaw("""
            {
              "UnlockKey": 19,
              "SomeFutureSetting": { "Nested": [1, 2, 3] },
              "AnotherOne": true
            }
            """);

        var settings = _store.Load();

        Assert.Equal(VirtualKeys.Pause, settings.UnlockKey);
        Assert.False(File.Exists(_store.BackupPath));
    }

    [Fact]
    public void Missing_fields_keep_their_defaults()
    {
        WriteRaw("""{ "ShowClock": false }""");

        var settings = _store.Load();

        Assert.False(settings.ShowClock);
        Assert.Equal(new AppSettings { ShowClock = false }, settings);
    }

    [Fact]
    public void An_empty_object_gives_defaults()
    {
        WriteRaw("{}");

        Assert.Equal(new AppSettings(), _store.Load());
    }

    [Fact]
    public void An_unknown_screen_style_falls_back_without_losing_the_file()
    {
        WriteRaw("""{ "ScreenStyle": "Holographic", "CustomMessage": "kept" }""");

        var settings = _store.Load();

        Assert.Equal(ScreenStyle.Dimmed, settings.ScreenStyle);
        Assert.Equal("kept", settings.CustomMessage);
    }

    [Fact]
    public void An_unreadable_hotkey_falls_back_without_losing_the_file()
    {
        WriteRaw("""{ "LockHotkey": "Ctrl+Nonsense", "CustomMessage": "kept" }""");

        var settings = _store.Load();

        Assert.Equal(HotkeyGesture.Default, settings.LockHotkey);
        Assert.Equal("kept", settings.CustomMessage);
    }

    [Fact]
    public void A_hotkey_of_the_wrong_type_falls_back_without_losing_the_file()
    {
        WriteRaw("""{ "LockHotkey": { "Key": 76 }, "ScreenStyle": [1], "CustomMessage": "kept" }""");

        var settings = _store.Load();

        Assert.Equal(HotkeyGesture.Default, settings.LockHotkey);
        Assert.Equal(ScreenStyle.Dimmed, settings.ScreenStyle);
        Assert.Equal("kept", settings.CustomMessage);
    }

    [Fact]
    public void A_null_message_reads_as_empty_without_losing_the_file()
    {
        WriteRaw("""{ "CustomMessage": null, "UnlockKey": 19 }""");

        var settings = _store.Load();

        Assert.Equal(string.Empty, settings.CustomMessage);
        Assert.Equal(VirtualKeys.Pause, settings.UnlockKey);
        Assert.Equal(SettingsLoadResult.Loaded, _store.LastLoad);
        Assert.False(File.Exists(_store.BackupPath));
    }

    [Fact]
    public void Field_names_are_case_insensitive()
    {
        WriteRaw("""{ "unlockkey": 19 }""");

        Assert.Equal(VirtualKeys.Pause, _store.Load().UnlockKey);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated_in_a_hand_edited_file()
    {
        WriteRaw("""
            {
              // Pause key
              "UnlockKey": 19,
            }
            """);

        Assert.Equal(VirtualKeys.Pause, _store.Load().UnlockKey);
    }

    [Fact]
    public void A_locked_file_gives_defaults_without_being_treated_as_corrupt()
    {
        _store.Save(new AppSettings { CustomMessage = "kept" });

        using (new FileStream(_store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(new AppSettings(), _store.Load());
            Assert.Equal(SettingsLoadResult.ReadFailed, _store.LastLoad);
        }

        Assert.False(File.Exists(_store.BackupPath));
        Assert.Equal("kept", _store.Load().CustomMessage);
        Assert.Equal(SettingsLoadResult.Loaded, _store.LastLoad);
    }

    [Fact]
    public void A_file_that_is_only_busy_for_a_moment_is_still_read()
    {
        _store.Save(new AppSettings { CustomMessage = "kept" });
        var holder = new FileStream(_store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // A thread of its own: on a busy thread pool, a timer can fire after the retries have run out.
        var release = new Thread(() =>
        {
            Thread.Sleep(30);
            holder.Dispose();
        });
        release.Start();

        var settings = _store.Load();
        release.Join();

        Assert.Equal("kept", settings.CustomMessage);
        Assert.Equal(SettingsLoadResult.Loaded, _store.LastLoad);
    }

    [Fact]
    public void A_missing_file_counts_as_loaded()
    {
        _store.Load();

        Assert.Equal(SettingsLoadResult.Loaded, _store.LastLoad);
    }

    [Fact]
    public void A_corrupt_file_is_reported()
    {
        WriteRaw("broken");

        _store.Load();

        Assert.Equal(SettingsLoadResult.Corrupt, _store.LastLoad);
    }

    [Fact]
    public void A_good_load_clears_the_last_problem()
    {
        WriteRaw("broken");
        _store.Load();
        _store.Save(new AppSettings());

        _store.Load();

        Assert.Equal(SettingsLoadResult.Loaded, _store.LastLoad);
    }

    [Fact]
    public void The_same_unlock_ignores_other_settings()
    {
        var settings = new AppSettings().WithPin(PinHasher.Create("1234", 1_000));

        Assert.True(settings.HasSameUnlock(settings with { CustomMessage = "changed", HasSeenTrayHint = true }));
    }

    [Fact]
    public void A_new_unlock_key_or_pin_is_a_different_unlock()
    {
        var settings = new AppSettings();

        Assert.False(settings.HasSameUnlock(settings with { UnlockKey = VirtualKeys.Pause }));
        Assert.False(settings.HasSameUnlock(settings.WithPin(PinHasher.Create("1234", 1_000))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_an_empty_directory(string? directory) =>
        Assert.ThrowsAny<ArgumentException>(() => new SettingsStore(directory!));
}
