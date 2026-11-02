using Inactive.Core.Native;

namespace Inactive.Core.Locking;

/// <summary>
/// Decides the fate of each input event. It runs inside the hook callbacks, on the hook thread
/// only, so it is single threaded and must stay fast and allocation free.
/// </summary>
internal sealed class InputFilter
{
    public const long AttemptThrottleMilliseconds = 2_000;

    // The keys of the lock hotkey often still repeat for a moment after locking. That is not
    // someone trying to get in, so the first signal waits this long.
    public const long AttemptGraceMilliseconds = 750;

    private const int KeyCount = 256;

    private readonly LockStateMachine _machine;
    private readonly PinnedPoint _pinnedPoint;
    private readonly Action _onInputAttempted;
    private readonly Func<long> _clock;
    private readonly Func<int, bool> _isDownInWindows;

    // Keys whose press Windows never saw, so their release must not reach anything either.
    private readonly bool[] _swallowRelease = new bool[KeyCount];
    private long _nextAttemptSignal;

    /// <param name="machine">The lock logic.</param>
    /// <param name="pinnedPoint">Where the cursor guard holds the cursor.</param>
    /// <param name="onInputAttempted">Called when someone tries the keyboard or mouse. Must not block.</param>
    /// <param name="clock">Returns a monotonic time in milliseconds.</param>
    /// <param name="isDownInWindows">
    /// Reports whether Windows already considers a key down. A low level hook runs before Windows
    /// updates that state, so this is false for a fresh press and true for a key held since before
    /// the lock. Must be allocation free.
    /// </param>
    public InputFilter(
        LockStateMachine machine,
        PinnedPoint pinnedPoint,
        Action onInputAttempted,
        Func<long> clock,
        Func<int, bool> isDownInWindows)
    {
        _machine = machine;
        _pinnedPoint = pinnedPoint;
        _onInputAttempted = onInputAttempted;
        _clock = clock;
        _isDownInWindows = isDownInWindows;
        _nextAttemptSignal = clock() + AttemptGraceMilliseconds;
    }

    public long KeyboardEventsSwallowed { get; private set; }

    public long MouseEventsSwallowed { get; private set; }

    /// <summary>Returns true when the key event must be swallowed.</summary>
    public bool OnKey(bool isKeyDown, int virtualKey, bool isExtended)
    {
        var isTrackedKey = (uint)virtualKey < KeyCount;

        // A key held while locking (such as the hotkey) was seen going down, so its release has to
        // pass or it would stay stuck. A key pressed during the lock was never seen by Windows, and
        // its release would show what was typed, PIN digits included, to the foreground app.
        if (!isKeyDown)
        {
            _machine.OnKeyUp(virtualKey);

            if (!isTrackedKey || !_swallowRelease[virtualKey])
            {
                return false;
            }

            _swallowRelease[virtualKey] = false;
            KeyboardEventsSwallowed++;
            return true;
        }

        var result = _machine.OnKeyDown(virtualKey, isExtended);
        if (result == KeyDownResult.NotLocked)
        {
            // Windows sees this press, so it must see the release as well.
            if (isTrackedKey)
            {
                _swallowRelease[virtualKey] = false;
            }

            return false;
        }

        if (result == KeyDownResult.Blocked)
        {
            SignalAttempt();
        }

        if (isTrackedKey)
        {
            _swallowRelease[virtualKey] = !_isDownInWindows(virtualKey);
        }

        KeyboardEventsSwallowed++;
        return true;
    }

    /// <summary>Returns true when the mouse event must be swallowed.</summary>
    public bool OnMouse(uint message, int x, int y, bool isInjected)
    {
        var phase = _machine.Phase;
        if (phase == LockPhase.Unlocked || IsButtonUp(message))
        {
            return false;
        }

        // SetCursorPos arrives here as an injected move. Swallowing CursorGuard's own move would
        // stop it from working and would look like someone using the mouse.
        if (message == NativeConstants.WM_MOUSEMOVE && isInjected && _pinnedPoint.IsAt(x, y))
        {
            return false;
        }

        if (phase is LockPhase.Locked or LockPhase.CoolingDown)
        {
            SignalAttempt();
        }

        MouseEventsSwallowed++;
        return true;
    }

    private void SignalAttempt()
    {
        var now = _clock();
        if (now >= _nextAttemptSignal)
        {
            _nextAttemptSignal = now + AttemptThrottleMilliseconds;
            _onInputAttempted();
        }
    }

    private static bool IsButtonUp(uint message) => message is
        NativeConstants.WM_LBUTTONUP or NativeConstants.WM_RBUTTONUP or
        NativeConstants.WM_MBUTTONUP or NativeConstants.WM_XBUTTONUP;
}
