using System.Windows;
using Inactive.Core.Locking;
using Inactive.Core.Logging;
using Inactive.Core.Settings;
using Inactive.Overlay;
using Inactive.Services;
using Inactive.Startup;
using Inactive.Updating;

namespace Inactive;

/// <summary>Builds the app and decides what happens when, from startup to exit.</summary>
internal sealed partial class App : Application
{
    private const string TrayHintTitle = "Inactive is still running";
    private const string TrayHintText = "It stays in the system tray, ready to lock. Right click the icon to exit.";
    private const string FaultTitle = "Your PC was unlocked";
    private const string FaultText = "Inactive ran into a problem and let go of the keyboard and mouse.";

    private readonly StartupOptions _options;
    private readonly EventWaitHandle _showWindowSignal;

    private SettingsService _settings = null!;
    private LockController _engine = null!;
    private LockCoordinator _locks = null!;
    private OverlayManager _overlays = null!;
    private GlobalHotkey _hotkey = null!;
    private UpdateService _updates = null!;
    private MainWindow _main = null!;
    private TrayIcon _tray = null!;
    private SystemEventBridge _systemEvents = null!;
    private RegisteredWaitHandle? _showWindowWait;

    private bool _restoreMainAfterLock;
    private string? _announcedUpdate;

    public App(StartupOptions options, EventWaitHandle showWindowSignal)
    {
        _options = options;
        _showWindowSignal = showWindowSignal;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        BuildServices();
        WireEvents();

        _main.ShowSettingsLoadProblem(_settings.LoadResult);
        _hotkey.Apply(_settings.Current.LockHotkey);

        // Keeps the entry pointing at this install if the app was moved or reinstalled.
        if (_settings.Current.StartWithWindows)
        {
            StartupRegistration.RefreshIfInstalled();
        }

#if DEBUG
        if (_options.DemoLockSeconds is { } seconds)
        {
            new Debugging.DemoLock(_engine, _locks, seconds, _options.DemoLogPath, ExitApp).Start();
            return;
        }
#endif

        _showWindowWait = ThreadPool.RegisterWaitForSingleObject(
            _showWindowSignal,
            (_, _) => Dispatcher.BeginInvoke(OnShowWindowSignal),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        // A load problem shows only on the home page, and a corrupt file is gone by the next start.
        if (!_options.StartHidden || _settings.LoadResult != SettingsLoadResult.Loaded)
        {
            ShowMainWindow();
        }

        _updates.Start();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _main.AllowClose = true;
        _engine.Release(UnlockReason.Shutdown);
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWindowWait?.Unregister(null);
        _systemEvents.Dispose();
        _engine.Dispose();
        _locks.Dispose();
        _hotkey.Dispose();
        _updates.Dispose();
        _tray.Dispose();

        base.OnExit(e);
    }

    private void BuildServices()
    {
        _settings = new SettingsService(new SettingsStore());
        _engine = new LockController();
        _overlays = new OverlayManager(() => _engine.CooldownRemaining);
        _locks = new LockCoordinator(_engine, _overlays, _settings, Dispatcher);
        _hotkey = new GlobalHotkey();
        _updates = new UpdateService(
            () => _locks.IsEngaged,
            () => _settings.Current.AutoUpdate,
            ExitApp);

        // Subscribed before any page, so that pages see the outcome when they refresh.
        _settings.Changed += (_, _) => _hotkey.Apply(_settings.Current.LockHotkey);
        _settings.UnlockChanged += (_, _) => _main.ClearUnlockNotices();

        _main = new MainWindow(new AppServices(_settings, _locks, _hotkey, _updates, RestartToUpdate));
        MainWindow = _main;

        _tray = new TrayIcon();
        _systemEvents = new SystemEventBridge(_engine, () => Dispatcher.BeginInvoke(OnEnvironmentChanged));
    }

    private void WireEvents()
    {
        _hotkey.Pressed += (_, _) => _locks.RequestLock();
        _updates.StateChanged += (_, _) => Dispatcher.BeginInvoke(OnUpdateStateChanged);

        _locks.Engaging += (_, _) => OnEngaging();
        _locks.Engaged += (_, _) => _tray.ShowLocked(true);
        _locks.Failed += (_, message) => OnLockFailed(message);
        _locks.Released += (_, e) => OnReleased(e.Reason);

        _main.HiddenToTray += (_, _) => OnHiddenToTray();

        _tray.LockRequested += (_, _) => _locks.RequestLock();
        _tray.ToggleWindowRequested += (_, _) => ToggleMainWindow();
        _tray.OpenRequested += (_, _) => ShowMainWindow();
        _tray.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesFromTrayAsync();
        _tray.ExitRequested += (_, _) => ExitFromTray();
    }

    private void ShowMainWindow()
    {
        if (_locks.IsEngaged)
        {
            _restoreMainAfterLock = true;
            return;
        }

        _main.ShowAndActivate();
    }

    private void ToggleMainWindow()
    {
        if (_main.IsVisible && _main.WindowState != WindowState.Minimized)
        {
            _main.Hide();
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void OnShowWindowSignal() => ShowMainWindow();

    private void OnEngaging()
    {
        _restoreMainAfterLock = _main.IsVisible;
        _main.ClearNotices();
        _main.Hide();
    }

    private void OnLockFailed(string message)
    {
        _main.ShowLockFailure(message);
        ShowMainWindow();
    }

    private void OnReleased(UnlockReason reason)
    {
        _tray.ShowLocked(false);

        if (_restoreMainAfterLock)
        {
            _main.ShowAndActivate();
        }

        if (reason == UnlockReason.EngineFault)
        {
            _main.ShowEngineFault();
            _tray.ShowToast(FaultTitle, FaultText);
        }

        // An update that became ready during the lock waited for this moment.
        OnUpdateStateChanged();
    }

    private void OnHiddenToTray()
    {
        if (!_settings.Current.HasSeenTrayHint)
        {
            _settings.Update(settings => settings with { HasSeenTrayHint = true });
            _tray.ShowToast(TrayHintTitle, TrayHintText);
        }
    }

    private void OnEnvironmentChanged()
    {
        _overlays.Rebuild();
        _overlays.ReassertTopmost();
    }

    private void OnUpdateStateChanged()
    {
        _main.ShowUpdateState();

        var version = _updates.ReadyVersion;
        if (version is not null && version != _announcedUpdate && !_locks.IsEngaged)
        {
            _announcedUpdate = version;
            _tray.ShowToast($"Inactive {version} is ready", "Select this to restart and update.", RestartToUpdate);
        }
    }

    private async Task CheckForUpdatesFromTrayAsync()
    {
        var announcedBefore = _announcedUpdate;
        var result = await _updates.CheckNowAsync();

        switch (result)
        {
            case UpdateCheckResult.Disabled:
                _tray.ShowToast("Updates are off", "Updates are off in development builds.");
                break;

            case UpdateCheckResult.UpToDate:
                _tray.ShowToast("You're up to date", $"Inactive {_updates.CurrentVersion} is the latest version.");
                break;

            case UpdateCheckResult.Failed:
                _tray.ShowToast("Couldn't check for updates", "Try again later.");
                break;

            case UpdateCheckResult.UpdateReady:
                // The toast that goes with a new version comes from StateChanged. If this version
                // had its toast before the check, the person asked again, so answer them.
                if (_updates.ReadyVersion == announcedBefore)
                {
                    _announcedUpdate = null;
                    OnUpdateStateChanged();
                }

                break;
        }
    }

    private void RestartToUpdate()
    {
        if (!_locks.IsEngaged)
        {
            _updates.RestartToApply();
        }
    }

    // Exiting ends the lock with no key or PIN, so the tray must not offer that as a way out.
    private void ExitFromTray()
    {
        if (!_locks.IsEngaged)
        {
            ExitApp();
        }
    }

    private void ExitApp()
    {
        // Shutdown only queues the exit, so a hotkey press already in the queue could still lock.
        _locks.RefuseLocks();
        _main.AllowClose = true;

        if (!_locks.IsEngaged)
        {
            _updates.ApplyOnExit();
        }

        Shutdown();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        AppLog.Error("Unhandled exception.", e.ExceptionObject as Exception);

    // Left unhandled on purpose. If the process dies, Windows removes the hooks and the PC is free,
    // which beats carrying on in an unknown state while holding the keyboard and mouse.
    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) =>
        AppLog.Error("Unhandled exception on the UI thread.", e.Exception);
}
