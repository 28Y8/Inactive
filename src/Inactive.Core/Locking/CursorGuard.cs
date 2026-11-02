namespace Inactive.Core.Locking;

internal readonly record struct ScreenSize(int Width, int Height);

/// <summary>Seam over the Win32 cursor calls.</summary>
internal interface ICursorApi
{
    /// <summary>The primary monitor size in physical pixels. Its top left corner is always (0, 0).</summary>
    ScreenSize GetPrimaryScreenSize();

    void SetPosition(int x, int y);
}

/// <summary>
/// Pins the cursor to the centre of the primary monitor on its own thread. The hooks already block
/// the mouse, so this is defence in depth for movement they cannot see. The centre is worked out on
/// every pass, which picks up a display change without any notification.
/// It deliberately does not use ClipCursor: a clip has no owner, so it outlives a killed process
/// and would leave the mouse stuck after the lock has failed open.
/// </summary>
internal sealed class CursorGuard
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(1);

    private readonly ICursorApi _api;
    private readonly PinnedPoint _pinnedPoint;
    private readonly TimeSpan _interval;
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _thread;
    private volatile bool _stopping;

    public CursorGuard(ICursorApi api, PinnedPoint pinnedPoint, TimeSpan? interval = null)
    {
        _api = api;
        _pinnedPoint = pinnedPoint;
        _interval = interval ?? TimeSpan.FromMilliseconds(50);
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Inactive cursor guard" };
        _thread.Start();
    }

    /// <summary>Reasserts the pin right away instead of at the next interval.</summary>
    public void Reassert() => _wake.Set();

    /// <summary>Stops the guard. Safe to call from any thread.</summary>
    public void Stop()
    {
        _stopping = true;
        _wake.Set();
        _thread?.Join(JoinTimeout);
    }

    private void Run()
    {
        while (!_stopping)
        {
            var size = _api.GetPrimaryScreenSize();
            var x = size.Width / 2;
            var y = size.Height / 2;

            // Published first, so the mouse hook recognises the move that follows.
            _pinnedPoint.Set(x, y);
            _api.SetPosition(x, y);

            _wake.WaitOne(_interval);
        }
    }
}
