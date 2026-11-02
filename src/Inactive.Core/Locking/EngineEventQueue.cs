using Inactive.Core.Logging;

namespace Inactive.Core.Locking;

internal enum EngineEventKind
{
    Locked,
    Unlocked,
    PhaseChanged,
    PinProgress,
    PinRejected,
    InputAttempted,
    ElevatedForeground,
    HookReinstallFailed,
}

/// <summary>One engine event. A plain value, so posting one allocates nothing.</summary>
internal readonly record struct EngineEvent(EngineEventKind Kind, int First = 0, long Second = 0);

/// <summary>
/// Moves events from the engine threads to one delivery thread. Posting never blocks and never
/// allocates, so a slow or hung subscriber cannot reach back into the hook. When the ring is full
/// the oldest event is dropped: the newest is always the one that describes the current state.
/// </summary>
internal sealed class EngineEventQueue
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Action<EngineEvent> _deliver;
    private readonly EngineEvent[] _ring;
    private int _head;
    private int _count;
    private Thread? _thread;
    private bool _stopping;

    public EngineEventQueue(Action<EngineEvent> deliver, int capacity = 128)
    {
        _deliver = deliver;
        _ring = new EngineEvent[capacity];
    }

    /// <summary>Starts the delivery thread if it is not running yet.</summary>
    public void EnsureStarted()
    {
        lock (_gate)
        {
            if (_thread is null && !_stopping)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Inactive lock events" };
                _thread.Start();
            }
        }
    }

    public void Post(EngineEvent engineEvent)
    {
        lock (_gate)
        {
            if (_count == _ring.Length)
            {
                _head = (_head + 1) % _ring.Length;
                _count--;
            }

            _ring[(_head + _count) % _ring.Length] = engineEvent;
            _count++;
        }

        _wake.Set();
    }

    /// <summary>Delivers what is queued, then ends the thread. Gives up on a stuck subscriber.</summary>
    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            _stopping = true;
            thread = _thread;
        }

        _wake.Set();
        thread?.Join(JoinTimeout);
    }

    private void Run()
    {
        while (true)
        {
            bool stop;
            while (TryTake(out var engineEvent, out stop))
            {
                Deliver(engineEvent);
            }

            if (stop)
            {
                return;
            }

            _wake.WaitOne();
        }
    }

    private bool TryTake(out EngineEvent engineEvent, out bool stopping)
    {
        lock (_gate)
        {
            stopping = _stopping;

            if (_count == 0)
            {
                engineEvent = default;
                return false;
            }

            engineEvent = _ring[_head];
            _head = (_head + 1) % _ring.Length;
            _count--;
            return true;
        }
    }

    private void Deliver(EngineEvent engineEvent)
    {
        try
        {
            _deliver(engineEvent);
        }
        catch (Exception e)
        {
            // A subscriber bug must not end event delivery for the rest of the session.
            AppLog.Error("A lock event subscriber failed.", e);
        }
    }
}
