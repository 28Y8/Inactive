using Inactive.Core.Input;
using Inactive.Core.Logging;
using Inactive.Core.Native;
using Inactive.Core.Power;
using Inactive.Core.Security;

namespace Inactive.Core.Locking;

/// <summary>
/// Runs the lock. All public members may be called from any thread. Every event is raised on one
/// dedicated background thread, in order, so subscribers marshal to the UI themselves. A subscriber
/// can delay later events but never the lock itself: the hook thread decides and releases alone.
/// </summary>
public sealed class LockController : IDisposable
{
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(2);

    private readonly Lock _lifecycle = new();
    private readonly ILockPlatform _platform;
    private readonly KeepAwake _keepAwake;
    private readonly EngineEventQueue _events;
    private readonly Func<long> _clock;
    private volatile Session? _session;
    private bool _disposed;

    public LockController()
        : this(new Win32LockPlatform())
    {
    }

    internal LockController(ILockPlatform platform)
    {
        _platform = platform;
        _clock = platform.NowMilliseconds;
        _keepAwake = new KeepAwake(platform.ExecutionState);
        _events = new EngineEventQueue(Deliver);
    }

    /// <summary>The hooks are on and input is blocked.</summary>
    public event EventHandler? Locked;

    /// <summary>The hooks are off and the cursor and power state are restored.</summary>
    public event EventHandler<UnlockedEventArgs>? Unlocked;

    public event EventHandler<LockPhaseChangedEventArgs>? PhaseChanged;

    /// <summary>The number of digits typed changed. Carries a count only, never a digit.</summary>
    public event EventHandler<PinProgressEventArgs>? PinProgress;

    public event EventHandler? PinRejected;

    /// <summary>Someone touched the keyboard or mouse. Raised at most once every 2 seconds.</summary>
    public event EventHandler? InputAttempted;

    /// <summary>
    /// An elevated window has focus, so the hooks may be blind. Activate the overlay. Repeats every
    /// 500 ms for as long as it stays true.
    /// </summary>
    public event EventHandler? ElevatedForeground;

    public LockPhase State => _session?.Machine.Phase ?? LockPhase.Unlocked;

    public bool IsLocked => State != LockPhase.Unlocked;

    /// <summary>The time left in the current cooldown, or zero.</summary>
    public TimeSpan CooldownRemaining => TimeSpan.FromMilliseconds(_session?.Machine.CooldownRemainingMilliseconds ?? 0);

#if DEBUG
    /// <summary>Counters from the last finished lock.</summary>
    public LockSessionStats LastSessionStats { get; private set; }
#endif

    /// <summary>
    /// Starts the lock and returns once the hooks are on. On failure nothing stays engaged. Create
    /// the overlay windows first, so that a failure there can never leave the hooks on.
    /// </summary>
    public LockResult TryLock(LockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_session is not null)
            {
                return new LockResult(LockError.AlreadyLocked);
            }

            if (UnlockKeyRules.Evaluate(request.UnlockKey) == UnlockKeyVerdict.Refused)
            {
                return new LockResult(LockError.InvalidUnlockKey);
            }

            if (request.Pin is not null && !PinHasher.IsUsable(request.Pin))
            {
                return new LockResult(LockError.InvalidPin);
            }

            _events.EnsureStarted();

            if (!_keepAwake.Acquire(request.AllowDisplayOff))
            {
                AppLog.Warn("Windows refused the keep awake request. Locking anyway.");
            }

            // Everything else goes on before the hooks, so that the hook thread alone owns the
            // teardown from the moment they are on, even if the unlock key is pressed right away.
            var session = new Session(this, request);
            session.Machine.Begin(_platform.IsKeyDown(request.UnlockKey));
            session.StartGuards();
            _session = session;

            var started = session.Blocker.Start(session.Filter, session);
            if (started.Installed)
            {
                AppLog.Info("Locked.");
                return new LockResult(LockError.None);
            }

            _session = null;
            session.StopGuards();
            _keepAwake.Release();
            session.Finished.Set();
            AppLog.Error($"Input hooks could not be installed, Win32 error {started.Win32Error}.");
            return new LockResult(LockError.HookInstallFailed, started.Win32Error);
        }
    }

    /// <summary>
    /// Ends the lock without a key or PIN, for session unlock, shutdown and exit. Blocks until the
    /// engine has let go, for at most 2 seconds. Returns false when nothing was locked.
    /// </summary>
    public bool Release(UnlockReason reason)
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        session.Machine.ForceUnlock(reason);

        if (!session.Finished.Wait(ReleaseTimeout))
        {
            AppLog.Error("The hook thread did not stop in time.");
        }

        return true;
    }

    /// <summary>
    /// Reasserts the hooks, the cursor pin and the keep awake state right away. Call after resume
    /// from sleep and after a display change.
    /// </summary>
    public void Reassert()
    {
        var session = _session;
        if (session is not null)
        {
            session.Reassert();
            _keepAwake.Reassert();
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Release(UnlockReason.Exit);
        _events.Stop();
        _keepAwake.Dispose();
    }

    private void Deliver(EngineEvent engineEvent)
    {
        switch (engineEvent.Kind)
        {
            case EngineEventKind.Locked:
                Locked?.Invoke(this, EventArgs.Empty);
                break;

            case EngineEventKind.Unlocked:
                AppLog.Info($"Unlocked: {(UnlockReason)engineEvent.First}.");
                Unlocked?.Invoke(this, new UnlockedEventArgs((UnlockReason)engineEvent.First));
                break;

            case EngineEventKind.PhaseChanged:
                PhaseChanged?.Invoke(this, new LockPhaseChangedEventArgs(
                    (LockPhase)engineEvent.First,
                    TimeSpan.FromMilliseconds(engineEvent.Second)));
                break;

            case EngineEventKind.PinProgress:
                PinProgress?.Invoke(this, new PinProgressEventArgs(engineEvent.First, (int)engineEvent.Second));
                break;

            case EngineEventKind.PinRejected:
                PinRejected?.Invoke(this, EventArgs.Empty);
                break;

            case EngineEventKind.InputAttempted:
                InputAttempted?.Invoke(this, EventArgs.Empty);
                break;

            case EngineEventKind.ElevatedForeground:
                ElevatedForeground?.Invoke(this, EventArgs.Empty);
                break;

            case EngineEventKind.HookReinstallFailed:
                AppLog.Warn($"Input hooks could not be refreshed, Win32 error {engineEvent.First}. The old hooks stay on.");
                break;
        }
    }

    /// <summary>One lock, from TryLock to the hooks coming off.</summary>
    private sealed class Session : ILockMachineSink, IBlockerHost
    {
        private readonly LockController _owner;
        private readonly CursorGuard _cursorGuard;
        private readonly ForegroundGuard _foregroundGuard;
        private readonly PinVerifier? _pinVerifier;
        private readonly EngineEventQueue _events;
        private UnlockReason _reason;

        public Session(LockController owner, LockRequest request)
        {
            _owner = owner;
            _events = owner._events;

            var pinnedPoint = new PinnedPoint();
            Machine = new LockStateMachine(request.UnlockKey, request.Pin?.Length ?? 0, this, owner._clock);
            Filter = new InputFilter(
                Machine,
                pinnedPoint,
                () => _events.Post(new EngineEvent(EngineEventKind.InputAttempted)),
                owner._clock,
                owner._platform.IsKeyDown);
            Blocker = owner._platform.CreateInputBlocker();

            _cursorGuard = new CursorGuard(owner._platform.Cursor, pinnedPoint);
            _foregroundGuard = new ForegroundGuard(
                owner._platform.Foreground,
                () => _events.Post(new EngineEvent(EngineEventKind.ElevatedForeground)));

            if (request.Pin is not null)
            {
                _pinVerifier = new PinVerifier(request.Pin, Machine.CompleteVerification);
            }
        }

        public LockStateMachine Machine { get; }

        public InputFilter Filter { get; }

        public IInputBlocker Blocker { get; }

        public ManualResetEventSlim Finished { get; } = new();

        public void StartGuards()
        {
            _pinVerifier?.Start();
            _cursorGuard.Start();
            _foregroundGuard.Start();
        }

        public void StopGuards()
        {
            _cursorGuard.Stop();
            _foregroundGuard.Stop();
            _pinVerifier?.Stop();
        }

        public void Reassert()
        {
            Blocker.RequestReinstall();
            _cursorGuard.Reassert();
        }

        void IBlockerHost.OnInstalled()
        {
            _events.Post(new EngineEvent(EngineEventKind.Locked));
            _events.Post(new EngineEvent(EngineEventKind.PhaseChanged, (int)LockPhase.Locked));
        }

        void IBlockerHost.OnTick() => Machine.Tick();

        void IBlockerHost.OnReinstallFailed(int win32Error) =>
            _events.Post(new EngineEvent(EngineEventKind.HookReinstallFailed, win32Error));

        // Runs on the hook thread after the hooks are off, so a hung UI cannot hold up the release.
        void IBlockerHost.OnStopped()
        {
            Machine.ForceUnlock(UnlockReason.EngineFault);
            StopGuards();
            _owner._keepAwake.Release();

#if DEBUG
            _owner.LastSessionStats = new LockSessionStats(Filter.KeyboardEventsSwallowed, Filter.MouseEventsSwallowed);
#endif

            _owner._session = null;
            _events.Post(new EngineEvent(EngineEventKind.Unlocked, (int)_reason));
            Finished.Set();
        }

        void ILockMachineSink.OnPhaseChanged(LockPhase phase, long cooldownMilliseconds) =>
            _events.Post(new EngineEvent(EngineEventKind.PhaseChanged, (int)phase, cooldownMilliseconds));

        void ILockMachineSink.OnPinProgress(int entered, int length) =>
            _events.Post(new EngineEvent(EngineEventKind.PinProgress, entered, length));

        void ILockMachineSink.OnPinRejected() =>
            _events.Post(new EngineEvent(EngineEventKind.PinRejected));

        void ILockMachineSink.OnVerificationRequested(ReadOnlySpan<char> pin, long token) =>
            _pinVerifier?.Submit(pin, token);

        void ILockMachineSink.OnUnlocked(UnlockReason reason) => _reason = reason;

        void ILockMachineSink.OnDrained() => Blocker.RequestStop();
    }
}
