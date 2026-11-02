using Inactive.Core.Locking;
using Inactive.Core.Power;

namespace Inactive.Tests.TestSupport;

internal sealed class FakeClock
{
    public long Now { get; set; } = 1_000_000;

    public void Advance(long milliseconds) => Now += milliseconds;

    public long Read() => Now;
}

/// <summary>Records everything the state machine reports, as readable lines.</summary>
internal sealed class RecordingSink : ILockMachineSink
{
    public List<string> Events { get; } = [];

    public List<(string Pin, long Token)> Verifications { get; } = [];

    public long LastToken => Verifications[^1].Token;

    public int DrainedCalls { get; private set; }

    public void OnPhaseChanged(LockPhase phase, long cooldownMilliseconds) =>
        Events.Add(phase == LockPhase.CoolingDown ? $"phase:{phase}:{cooldownMilliseconds}" : $"phase:{phase}");

    public void OnPinProgress(int entered, int length) => Events.Add($"progress:{entered}/{length}");

    public void OnPinRejected() => Events.Add("rejected");

    public void OnVerificationRequested(ReadOnlySpan<char> pin, long token)
    {
        Verifications.Add((pin.ToString(), token));
        Events.Add("verify");
    }

    public void OnUnlocked(UnlockReason reason) => Events.Add($"unlocked:{reason}");

    public void OnDrained() => DrainedCalls++;
}

internal sealed class FakeCursorApi : ICursorApi
{
    private int _setPositionCalls;

    public volatile int Width = 1920;
    public volatile int Height = 1080;

    public (int X, int Y) LastPosition { get; private set; }

    public int SetPositionCalls => Volatile.Read(ref _setPositionCalls);

    public ScreenSize GetPrimaryScreenSize() => new(Width, Height);

    public void SetPosition(int x, int y)
    {
        LastPosition = (x, y);
        Interlocked.Increment(ref _setPositionCalls);
    }
}

internal sealed class FakeExecutionStateApi : IExecutionStateApi
{
    private readonly Lock _gate = new();
    private readonly List<(uint Flags, int ThreadId)> _calls = [];

    public uint Result { get; set; } = 1;

    public IReadOnlyList<(uint Flags, int ThreadId)> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public uint SetExecutionState(uint flags)
    {
        lock (_gate)
        {
            _calls.Add((flags, Environment.CurrentManagedThreadId));
        }

        return Result;
    }
}

internal sealed class FakeForegroundProbe : IForegroundProbe
{
    public volatile bool Elevated;

    public bool IsForegroundElevated() => Elevated;
}

/// <summary>Stands in for the hook thread: stops on a thread of its own, as the real one does.</summary>
internal sealed class FakeInputBlocker : IInputBlocker
{
    private readonly ManualResetEventSlim _stop = new();
    private int _reinstallRequests;

    public HookStartResult StartResult { get; init; } = new(true, 0);

    public InputFilter? Filter { get; private set; }

    public IBlockerHost? Host { get; private set; }

    public int ReinstallRequests => Volatile.Read(ref _reinstallRequests);

    public bool HangOnStop { get; init; }

    public HookStartResult Start(InputFilter filter, IBlockerHost host)
    {
        Filter = filter;
        Host = host;

        if (!StartResult.Installed)
        {
            return StartResult;
        }

        host.OnInstalled();

        new Thread(() =>
        {
            _stop.Wait();
            if (!HangOnStop)
            {
                host.OnStopped();
            }
        })
        { IsBackground = true }.Start();

        return StartResult;
    }

    public void RequestStop() => _stop.Set();

    public void RequestReinstall() => Interlocked.Increment(ref _reinstallRequests);
}

internal sealed class FakeLockPlatform : ILockPlatform
{
    private readonly Lock _gate = new();
    private readonly HashSet<int> _keysDown = [];
    private long _now = 1_000_000;

    public FakeCursorApi FakeCursor { get; } = new();

    public FakeForegroundProbe FakeForeground { get; } = new();

    public FakeExecutionStateApi FakeExecutionState { get; } = new();

    public FakeInputBlocker Blocker { get; set; } = new();

    public ICursorApi Cursor => FakeCursor;

    public IForegroundProbe Foreground => FakeForeground;

    public IExecutionStateApi ExecutionState => FakeExecutionState;

    public IInputBlocker CreateInputBlocker() => Blocker;

    public void HoldKey(int virtualKey)
    {
        lock (_gate)
        {
            _keysDown.Add(virtualKey);
        }
    }

    public bool IsKeyDown(int virtualKey)
    {
        lock (_gate)
        {
            return _keysDown.Contains(virtualKey);
        }
    }

    public long NowMilliseconds() => Volatile.Read(ref _now);

    public void Advance(long milliseconds) => Interlocked.Add(ref _now, milliseconds);
}
