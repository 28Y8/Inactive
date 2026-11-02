using Inactive.Core.Native;

namespace Inactive.Core.Power;

/// <summary>Seam over SetThreadExecutionState.</summary>
internal interface IExecutionStateApi
{
    /// <summary>Applies the flags to the calling thread. Returns 0 on failure.</summary>
    uint SetExecutionState(uint flags);
}

/// <summary>
/// Keeps the PC and display awake. The execution state belongs to the thread that set it, so a
/// dedicated long lived thread owns it and reapplies it on a timer.
/// </summary>
internal sealed class KeepAwake : IDisposable
{
    private const uint Released = NativeConstants.ES_CONTINUOUS;
    private static readonly TimeSpan AcknowledgeTimeout = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly IExecutionStateApi _api;
    private readonly TimeSpan _reapplyInterval;

    private Thread? _thread;
    private uint _flags = Released;
    private long _requested;
    private long _applied;
    private bool _lastSucceeded = true;
    private bool _disposed;

    public KeepAwake(IExecutionStateApi api, TimeSpan? reapplyInterval = null)
    {
        _api = api;
        _reapplyInterval = reapplyInterval ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>Starts keeping the PC awake. Returns false when Windows refused.</summary>
    public bool Acquire(bool allowDisplayOff)
    {
        var flags = NativeConstants.ES_CONTINUOUS | NativeConstants.ES_SYSTEM_REQUIRED;
        if (!allowDisplayOff)
        {
            flags |= NativeConstants.ES_DISPLAY_REQUIRED;
        }

        return Request(flags);
    }

    /// <summary>Lets Windows sleep normally again.</summary>
    public void Release() => Request(Released);

    /// <summary>Applies the current state again right away, for example after resume from sleep.</summary>
    public void Reassert()
    {
        lock (_gate)
        {
            if (_thread is not null && !_disposed)
            {
                Monitor.PulseAll(_gate);
            }
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _flags = Released;
            thread = _thread;
            Monitor.PulseAll(_gate);
        }

        thread?.Join(AcknowledgeTimeout);
    }

    private bool Request(uint flags)
    {
        lock (_gate)
        {
            // Dispose already released, and a late teardown on the hook thread must not throw.
            if (_disposed)
            {
                return false;
            }

            _flags = flags;
            var request = ++_requested;

            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Inactive keep awake" };
                _thread.Start();
            }

            Monitor.PulseAll(_gate);

            var deadline = Environment.TickCount64 + (long)AcknowledgeTimeout.TotalMilliseconds;
            while (_applied < request)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0 || !Monitor.Wait(_gate, (int)remaining))
                {
                    return false;
                }
            }

            return _lastSucceeded;
        }
    }

    private void Run()
    {
        lock (_gate)
        {
            while (true)
            {
                var request = _requested;
                _lastSucceeded = _api.SetExecutionState(_flags) != 0;
                _applied = request;
                Monitor.PulseAll(_gate);

                if (_disposed)
                {
                    return;
                }

                var timeout = _flags == Released ? Timeout.InfiniteTimeSpan : _reapplyInterval;
                Monitor.Wait(_gate, timeout);
            }
        }
    }
}
