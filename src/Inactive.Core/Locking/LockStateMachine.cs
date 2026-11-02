using Inactive.Core.Input;
using Inactive.Core.Security;

namespace Inactive.Core.Locking;

/// <summary>What the state machine reports as it moves. Called inside its lock, so must not block.</summary>
internal interface ILockMachineSink
{
    void OnPhaseChanged(LockPhase phase, long cooldownMilliseconds);

    void OnPinProgress(int entered, int length);

    void OnPinRejected();

    /// <summary>Copy the digits before returning: the buffer behind the span is wiped right after.</summary>
    void OnVerificationRequested(ReadOnlySpan<char> pin, long token);

    void OnUnlocked(UnlockReason reason);

    /// <summary>The lock is over and the keys of the unlock have settled, so the hooks can come off.</summary>
    void OnDrained();
}

/// <summary>How the state machine treated a key press.</summary>
internal enum KeyDownResult
{
    /// <summary>The machine is unlocked, so the key should reach the system.</summary>
    NotLocked,

    /// <summary>Auto repeat of a key that is already held.</summary>
    Repeat,

    /// <summary>The key was meaningful here.</summary>
    Consumed,

    /// <summary>A fresh press with no meaning here: someone is trying to use the locked PC.</summary>
    Blocked,
}

/// <summary>
/// The lock logic, free of Win32 and of real time. Every public member takes an internal lock, so
/// the hook thread, the PIN verifier and the releasing thread can all call in. Critical sections
/// are short and allocation free, which keeps them safe to enter from the keyboard hook. A new
/// instance is used for each lock session.
/// </summary>
internal sealed class LockStateMachine
{
    public const long PinIdleTimeoutMilliseconds = 20_000;
    public const long FirstCooldownMilliseconds = 30_000;
    public const long MaxCooldownMilliseconds = 300_000;
    public const int WrongAttemptsBeforeCooldown = 5;

    // Longer than the slowest Windows keyboard delay (1 s). A key still marked as held after this
    // long lost its key up, which happens while the secure desktop is showing, and must count as a
    // fresh press or the unlock key could stop working.
    public const long RepeatWindowMilliseconds = 1_500;

    // How long the hooks may stay on after the unlock key or PIN, to catch what the person's hands
    // are still doing. The hook thread applies this on its next tick, so it can run a tick longer.
    public const long DrainMilliseconds = 500;

    private const int KeyCount = 256;

    private readonly Lock _gate = new();
    private readonly ILockMachineSink _sink;
    private readonly Func<long> _clock;
    private readonly int _unlockKey;
    private readonly int _pinLength;

    private readonly bool[] _keyHeld = new bool[KeyCount];
    private readonly long[] _lastKeyDown = new long[KeyCount];

    // Pinned so the garbage collector cannot leave stray copies of the digits behind.
    private readonly char[] _digits = GC.AllocateArray<char>(PinHasher.MaxLength, pinned: true);

    private volatile LockPhase _phase = LockPhase.Unlocked;
    private int _entered;
    private long _idleDeadline;
    private long _verificationToken;
    private int _wrongAttempts;
    private long _nextCooldown = FirstCooldownMilliseconds;
    private long _cooldownEnd;
    private bool _isDraining;
    private long _drainEnd;

    /// <param name="unlockKey">The virtual key that ends the lock or starts PIN entry.</param>
    /// <param name="pinLength">The stored PIN length, or 0 when no PIN is configured.</param>
    /// <param name="sink">Receives transitions.</param>
    /// <param name="clock">Returns a monotonic time in milliseconds.</param>
    public LockStateMachine(int unlockKey, int pinLength, ILockMachineSink sink, Func<long> clock)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unlockKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(unlockKey, KeyCount);

        if (pinLength != 0 && pinLength is < PinHasher.MinLength or > PinHasher.MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(pinLength));
        }

        _unlockKey = unlockKey;
        _pinLength = pinLength;
        _sink = sink;
        _clock = clock;
    }

    public LockPhase Phase => _phase;

    internal bool DigitBufferIsClear
    {
        get
        {
            lock (_gate)
            {
                return _entered == 0 && Array.TrueForAll(_digits, c => c == '\0');
            }
        }
    }

    public long CooldownRemainingMilliseconds
    {
        get
        {
            lock (_gate)
            {
                return _phase == LockPhase.CoolingDown ? Math.Max(0, _cooldownEnd - _clock()) : 0;
            }
        }
    }

    /// <summary>
    /// Enters <see cref="LockPhase.Locked"/>. This raises nothing: the caller announces the lock
    /// once the hooks are really on, so a failed start stays silent.
    /// </summary>
    /// <param name="unlockKeyHeld">The unlock key is already down, so it must be released first.</param>
    public void Begin(bool unlockKeyHeld)
    {
        lock (_gate)
        {
            if (_phase != LockPhase.Unlocked)
            {
                throw new InvalidOperationException("The machine has already started.");
            }

            if (unlockKeyHeld)
            {
                _keyHeld[_unlockKey] = true;
                _lastKeyDown[_unlockKey] = _clock();
            }

            _phase = LockPhase.Locked;
        }
    }

    public KeyDownResult OnKeyDown(int virtualKey, bool isExtended)
    {
        lock (_gate)
        {
            if (_phase == LockPhase.Unlocked)
            {
                return HandleDrainKey(virtualKey);
            }

            var now = _clock();
            ExpireDeadlines(now);

            if (MarkKeyDown(virtualKey, now))
            {
                return KeyDownResult.Repeat;
            }

            return _phase switch
            {
                LockPhase.Locked => HandleLockedKey(virtualKey, now),
                LockPhase.PinEntry => HandlePinKey(virtualKey, isExtended, now),
                LockPhase.VerifyingPin => HandleVerifyingKey(virtualKey),
                _ => KeyDownResult.Blocked,
            };
        }
    }

    public void OnKeyUp(int virtualKey)
    {
        if ((uint)virtualKey >= KeyCount)
        {
            return;
        }

        lock (_gate)
        {
            _keyHeld[virtualKey] = false;

            if (_isDraining && !AnyKeyHeld())
            {
                FinishDrain();
            }
        }
    }

    /// <summary>Applies timeouts. Called regularly from the hook thread.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            ExpireDeadlines(_clock());
        }
    }

    /// <summary>Delivers a PIN check. A result whose token is no longer current is ignored.</summary>
    public void CompleteVerification(long token, bool accepted)
    {
        lock (_gate)
        {
            if (_phase != LockPhase.VerifyingPin || token != _verificationToken)
            {
                return;
            }

            if (accepted)
            {
                Unlock(UnlockReason.Pin);
                return;
            }

            _wrongAttempts++;
            _sink.OnPinRejected();

            var now = _clock();
            if (_wrongAttempts >= WrongAttemptsBeforeCooldown)
            {
                StartCooldown(now);
            }
            else
            {
                _idleDeadline = now + PinIdleTimeoutMilliseconds;
                SetPhase(LockPhase.PinEntry);
                _sink.OnPinProgress(0, _pinLength);
            }
        }
    }

    /// <summary>Ends the lock from any phase, without a key or a PIN. Does nothing when unlocked.</summary>
    public void ForceUnlock(UnlockReason reason)
    {
        lock (_gate)
        {
            if (_phase != LockPhase.Unlocked)
            {
                Unlock(reason);
            }
            else if (_isDraining)
            {
                FinishDrain();
            }
        }
    }

    private KeyDownResult HandleLockedKey(int virtualKey, long now)
    {
        if (virtualKey != _unlockKey)
        {
            return KeyDownResult.Blocked;
        }

        if (_pinLength == 0)
        {
            Unlock(UnlockReason.UnlockKey);
        }
        else
        {
            _idleDeadline = now + PinIdleTimeoutMilliseconds;
            SetPhase(LockPhase.PinEntry);
            _sink.OnPinProgress(0, _pinLength);
        }

        return KeyDownResult.Consumed;
    }

    private KeyDownResult HandlePinKey(int virtualKey, bool isExtended, long now)
    {
        _idleDeadline = now + PinIdleTimeoutMilliseconds;

        switch (virtualKey)
        {
            case VirtualKeys.Escape:
                ReturnToLocked();
                break;

            case VirtualKeys.Backspace when _entered > 0:
                _digits[--_entered] = '\0';
                _sink.OnPinProgress(_entered, _pinLength);
                break;

            case VirtualKeys.Enter when _entered > 0:
                Submit(now);
                break;

            default:
                if (KeyNames.TryGetPinDigit(virtualKey, isExtended, out var digit))
                {
                    _digits[_entered++] = digit;
                    _sink.OnPinProgress(_entered, _pinLength);

                    if (_entered == _pinLength)
                    {
                        Submit(now);
                    }
                }

                break;
        }

        return KeyDownResult.Consumed;
    }

    private KeyDownResult HandleVerifyingKey(int virtualKey)
    {
        if (virtualKey == VirtualKeys.Escape)
        {
            ReturnToLocked();
        }

        return KeyDownResult.Consumed;
    }

    private void Submit(long now)
    {
        _verificationToken++;
        _idleDeadline = now + PinIdleTimeoutMilliseconds;
        SetPhase(LockPhase.VerifyingPin);

        try
        {
            _sink.OnVerificationRequested(_digits.AsSpan(0, _entered), _verificationToken);
        }
        finally
        {
            ClearDigits();
        }
    }

    private void ReturnToLocked()
    {
        _verificationToken++;
        ClearDigits();
        SetPhase(LockPhase.Locked);
    }

    private void StartCooldown(long now)
    {
        _cooldownEnd = now + _nextCooldown;
        _nextCooldown = Math.Min(_nextCooldown * 2, MaxCooldownMilliseconds);
        ClearDigits();
        SetPhase(LockPhase.CoolingDown);
    }

    private void Unlock(UnlockReason reason)
    {
        _verificationToken++;
        ClearDigits();
        SetPhase(LockPhase.Unlocked);
        _sink.OnUnlocked(reason);

        // Someone is at the keyboard, so a held last digit would auto repeat into the foreground
        // app. The other reasons have nobody typing, or cannot wait.
        if (reason is UnlockReason.UnlockKey or UnlockReason.Pin && AnyKeyHeld())
        {
            _isDraining = true;
            _drainEnd = _clock() + DrainMilliseconds;
        }
        else
        {
            _sink.OnDrained();
        }
    }

    private KeyDownResult HandleDrainKey(int virtualKey)
    {
        if (!_isDraining
            || (uint)virtualKey >= KeyCount
            || (virtualKey != VirtualKeys.Enter && !_keyHeld[virtualKey]))
        {
            return KeyDownResult.NotLocked;
        }

        _keyHeld[virtualKey] = true;
        return KeyDownResult.Repeat;
    }

    private void FinishDrain()
    {
        _isDraining = false;
        _sink.OnDrained();
    }

    private bool AnyKeyHeld() => Array.IndexOf(_keyHeld, true) >= 0;

    private void ExpireDeadlines(long now)
    {
        switch (_phase)
        {
            case LockPhase.Unlocked when _isDraining && now >= _drainEnd:
                FinishDrain();
                break;

            case LockPhase.PinEntry or LockPhase.VerifyingPin when now >= _idleDeadline:
                ReturnToLocked();
                break;

            case LockPhase.CoolingDown when now >= _cooldownEnd:
                _wrongAttempts = 0;
                SetPhase(LockPhase.Locked);
                break;
        }
    }

    // Returns true when the press is an auto repeat. The low level hook reports repeats as plain
    // key downs, so held keys are tracked here.
    private bool MarkKeyDown(int virtualKey, long now)
    {
        if ((uint)virtualKey >= KeyCount)
        {
            return false;
        }

        var isRepeat = _keyHeld[virtualKey] && now - _lastKeyDown[virtualKey] < RepeatWindowMilliseconds;
        _keyHeld[virtualKey] = true;
        _lastKeyDown[virtualKey] = now;
        return isRepeat;
    }

    private void SetPhase(LockPhase phase)
    {
        _phase = phase;
        var cooldown = phase == LockPhase.CoolingDown ? Math.Max(0, _cooldownEnd - _clock()) : 0;
        _sink.OnPhaseChanged(phase, cooldown);
    }

    private void ClearDigits()
    {
        Array.Clear(_digits);
        _entered = 0;
    }
}
