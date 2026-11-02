using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using Inactive.Core.Locking;
using Inactive.Core.Logging;
using Inactive.Dialogs;
using Inactive.Native;
using Inactive.Overlay;

namespace Inactive.Services;

/// <summary>
/// Puts the UI and the lock engine together: overlays first, then the hooks. Everything comes back
/// when the engine lets go. Used from the UI thread, except for <see cref="IsEngaged"/>.
/// </summary>
internal sealed class LockCoordinator : IDisposable
{
    private const string OverlayFailedMessage = "Inactive couldn't cover the screen, so your PC was not locked.";

    private static readonly TimeSpan OverlayCloseDeadline = TimeSpan.FromSeconds(5);

    private readonly LockController _engine;
    private readonly OverlayManager _overlays;
    private readonly SettingsService _settings;
    private readonly Dispatcher _dispatcher;
    private volatile bool _isEngaged;
    private bool _isClosing;

    public LockCoordinator(LockController engine, OverlayManager overlays, SettingsService settings, Dispatcher dispatcher)
    {
        _engine = engine;
        _overlays = overlays;
        _settings = settings;
        _dispatcher = dispatcher;

        // The engine raises these on its own thread, in order. BeginInvoke keeps that order and
        // never blocks, so the UI thread can sit in Release while they queue.
        _engine.PhaseChanged += OnPhaseChanged;
        _engine.PinProgress += OnPinProgress;
        _engine.PinRejected += OnPinRejected;
        _engine.InputAttempted += OnInputAttempted;
        _engine.ElevatedForeground += OnElevatedForeground;
        _engine.Unlocked += OnUnlocked;
    }

    /// <summary>
    /// True from the moment a lock starts until the UI is restored. This outlasts
    /// <see cref="LockController.IsLocked"/>, which turns false a moment before teardown ends.
    /// </summary>
    public bool IsEngaged => _isEngaged;

    /// <summary>A lock is about to start, so get the main window out of the way.</summary>
    public event EventHandler? Engaging;

    /// <summary>The hooks are on.</summary>
    public event EventHandler? Engaged;

    /// <summary>The attempt to lock failed. Carries the message to show.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>The lock ended and the overlays are gone.</summary>
    public event EventHandler<UnlockedEventArgs>? Released;

    /// <summary>The app is on its way out, so from now on nothing may start a lock.</summary>
    public void RefuseLocks() => _isClosing = true;

    /// <summary>Locks, after the first lock confirmation if it has not been seen yet.</summary>
    public void RequestLock()
    {
        // A modal dialog may be recording a key or editing the PIN, which a lock would strand.
        if (_isClosing || _isEngaged || ComponentDispatcher.IsThreadModal)
        {
            return;
        }

        // Every way to unlock needs a key press. Windows cannot say for certain that no keyboard is
        // attached, so this warns and leaves the choice to the person.
        if (NativeMethods.IsInTabletMode() && !DialogHost.ShowModal(new NoKeyboardDialog()))
        {
            return;
        }

        if (!_settings.Current.HasSeenFirstLockHint)
        {
            var confirmation = new FirstLockDialog(_settings.Current);
            if (!DialogHost.ShowModal(confirmation))
            {
                return;
            }

            _settings.Update(settings => settings with { HasSeenFirstLockHint = true });
        }

        Engage();
    }

    /// <summary>Locks right away and reports the outcome.</summary>
    public LockResult Engage()
    {
        if (_isEngaged)
        {
            return new LockResult(LockError.AlreadyLocked);
        }

        _isEngaged = true;
        Engaging?.Invoke(this, EventArgs.Empty);

        var settings = _settings.Current;

        try
        {
            _overlays.Show(settings);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            AppLog.Error("The lock overlay could not be created.", e);
            Abort(OverlayFailedMessage);
            return new LockResult(LockError.HookInstallFailed);
        }

        var result = _engine.TryLock(LockRequest.FromSettings(settings));
        if (result.Succeeded)
        {
            Engaged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Abort(result.Message);
        }

        return result;
    }

    public void Dispose()
    {
        _engine.PhaseChanged -= OnPhaseChanged;
        _engine.PinProgress -= OnPinProgress;
        _engine.PinRejected -= OnPinRejected;
        _engine.InputAttempted -= OnInputAttempted;
        _engine.ElevatedForeground -= OnElevatedForeground;
        _engine.Unlocked -= OnUnlocked;
    }

    private void Abort(string message)
    {
        _overlays.Close();
        _isEngaged = false;
        Failed?.Invoke(this, message);
    }

    private void OnPhaseChanged(object? sender, LockPhaseChangedEventArgs e) =>
        _dispatcher.BeginInvoke(() => _overlays.SetPhase(e.Phase, e.CooldownRemaining));

    private void OnPinProgress(object? sender, PinProgressEventArgs e) =>
        _dispatcher.BeginInvoke(() => _overlays.SetPinProgress(e.Entered, e.Length));

    private void OnPinRejected(object? sender, EventArgs e) =>
        _dispatcher.BeginInvoke(_overlays.RejectPin);

    private void OnInputAttempted(object? sender, EventArgs e) =>
        _dispatcher.BeginInvoke(_overlays.ShowInputAttempt);

    private void OnElevatedForeground(object? sender, EventArgs e) =>
        _dispatcher.BeginInvoke(_overlays.TakeForeground);

    private void OnUnlocked(object? sender, UnlockedEventArgs e)
    {
        var teardown = _dispatcher.BeginInvoke(() =>
        {
            _overlays.Close();
            _isEngaged = false;
            Released?.Invoke(this, e);
        });

        // During an exit the dispatcher may never get to it, and the overlays go with the process.
        if (e.Reason is UnlockReason.Exit or UnlockReason.Shutdown)
        {
            return;
        }

        // Only the UI thread can close the overlays. If it has hung, ending the process is the way
        // to uncover the screen, in line with failing open. Waiting is safe: this is the engine's
        // event thread and the lock is already over. An aborted operation means the app is closing.
        if (teardown.Wait(OverlayCloseDeadline) is DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing)
        {
            AppLog.Error("The UI did not remove the lock overlay in time. Ending the process to clear the screen.");
            Process.GetCurrentProcess().Kill();
        }
    }
}
