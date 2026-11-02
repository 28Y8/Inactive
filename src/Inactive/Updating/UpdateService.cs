using System.Reflection;
using System.Windows;
using Inactive.Core.Logging;
using Inactive.Core.Settings;
using Velopack;
using Velopack.Sources;

namespace Inactive.Updating;

/// <summary>
/// Finds and downloads new versions in the background. Applying always ends this process, which
/// would drop the lock, so it only happens at the user's request or at exit and never while locked.
/// </summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
    // Installed copies trust whatever this address serves, so neither name may ever be given up.
    private const string RepositoryUrl = "https://github.com/28Y8/Inactive";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly Func<bool> _isLocked;
    private readonly Func<bool> _isAutoUpdateEnabled;
    private readonly Action _requestExit;
    private readonly UpdateManager? _manager;
    private readonly CancellationTokenSource _stopping = new();

    // Velopack's download takes a global lock and fails if a second one overlaps it.
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    private string? _readyVersion;
    private int _started;
    private int _applyStarted;

    /// <param name="isLocked">Reports whether the PC is locked right now.</param>
    /// <param name="isAutoUpdateEnabled">
    /// Reports the AutoUpdate setting before each scheduled check. Defaults to reading the settings file.
    /// </param>
    /// <param name="requestExit">
    /// Closes the app normally. Defaults to shutting down the WPF application. Velopack's own
    /// restart ends the process on the spot, which would leave a dead icon in the tray.
    /// </param>
    public UpdateService(Func<bool> isLocked, Func<bool>? isAutoUpdateEnabled = null, Action? requestExit = null)
    {
        ArgumentNullException.ThrowIfNull(isLocked);

        _isLocked = isLocked;
        _isAutoUpdateEnabled = isAutoUpdateEnabled ?? (() => new SettingsStore().Load().AutoUpdate);
        _requestExit = requestExit ?? ShutDownApplication;
        _manager = CreateManager();
    }

    public event EventHandler? StateChanged;

    public bool IsEnabled => _manager is not null;

    public string CurrentVersion => _manager?.CurrentVersion?.ToString() ?? AssemblyVersion();

    public string? ReadyVersion => Volatile.Read(ref _readyVersion);

    public void Start()
    {
        if (_manager is null || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() => RunScheduleAsync(_stopping.Token));
    }

    public async Task<UpdateCheckResult> CheckNowAsync()
    {
        if (_manager is null)
        {
            return UpdateCheckResult.Disabled;
        }

        var cancellation = _stopping.Token;
        try
        {
            await _checkGate.WaitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Failed;
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is not null)
            {
                await _manager.DownloadUpdatesAsync(update, cancelToken: cancellation).ConfigureAwait(false);
            }

            RefreshReadyVersion();
            return ReadyVersion is null ? UpdateCheckResult.UpToDate : UpdateCheckResult.UpdateReady;
        }
        catch (Exception e)
        {
            AppLog.Warn($"Update check failed: {e.Message}");

            // A failed download can take the package that was already waiting with it.
            RefreshReadyVersion();
            return ReadyVersion is null ? UpdateCheckResult.Failed : UpdateCheckResult.UpdateReady;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    public void RestartToApply()
    {
        if (TryStartUpdater(silent: false, restart: true))
        {
            _requestExit();
        }
        else
        {
            // The package behind a banner can be gone by now, so a dead banner clears itself.
            RefreshReadyVersion();
        }
    }

    public void ApplyOnExit() => TryStartUpdater(silent: true, restart: false);

    public void Dispose() => _stopping.Cancel();

    private static UpdateManager? CreateManager()
    {
        try
        {
            var manager = new UpdateManager(CreateSource());
            return manager.IsInstalled ? manager : null;
        }
        catch (Exception e)
        {
            AppLog.Error("The updater could not start.", e);
            return null;
        }
    }

    private static IUpdateSource CreateSource()
    {
#if DEBUG
        var feed = Environment.GetEnvironmentVariable("INACTIVE_UPDATE_FEED");
        if (!string.IsNullOrWhiteSpace(feed))
        {
            return new SimpleFileSource(new System.IO.DirectoryInfo(feed));
        }
#endif
        return new GithubSource(RepositoryUrl, accessToken: null, prerelease: false);
    }

    private static string AssemblyVersion()
    {
        var version = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        // The SDK appends "+<commit>" to the informational version.
        return version.Split('+')[0];
    }

    private static void ShutDownApplication() =>
        Application.Current?.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());

    private async Task RunScheduleAsync(CancellationToken cancellation)
    {
        try
        {
            // An update that an earlier run downloaded may still be waiting.
            RefreshReadyVersion();

            await Task.Delay(FirstCheckDelay, cancellation).ConfigureAwait(false);

            using var timer = new PeriodicTimer(CheckInterval);
            do
            {
                if (_isAutoUpdateEnabled())
                {
                    await CheckNowAsync().ConfigureAwait(false);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellation).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Closing down.
        }
        catch (Exception e)
        {
            AppLog.Error("The update schedule stopped.", e);
        }
    }

    private void RefreshReadyVersion()
    {
        var version = _manager?.UpdatePendingRestart?.Version.ToString();
        if (Interlocked.Exchange(ref _readyVersion, version) != version)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool TryStartUpdater(bool silent, bool restart)
    {
        if (_manager is null)
        {
            return false;
        }

        if (_isLocked())
        {
            AppLog.Info("Update not applied because the PC is locked.");
            return false;
        }

        try
        {
            var update = _manager.UpdatePendingRestart;
            if (update is null)
            {
                AppLog.Info("No downloaded update is waiting.");
                return false;
            }

            if (Interlocked.Exchange(ref _applyStarted, 1) != 0)
            {
                return false;
            }

            _manager.WaitExitThenApplyUpdates(update, silent, restart);
            return true;
        }
        catch (Exception e)
        {
            Volatile.Write(ref _applyStarted, 0);
            AppLog.Error("The updater could not be started.", e);
            return false;
        }
    }
}
