using Inactive.Core.Logging;

namespace Inactive.Core.Locking;

/// <summary>Seam over the elevation check.</summary>
internal interface IForegroundProbe
{
    /// <summary>
    /// True when the foreground window belongs to an elevated process, or to one that cannot be
    /// opened because access is denied.
    /// </summary>
    bool IsForegroundElevated();
}

/// <summary>
/// Low level hooks see no input while an elevated window has focus. This guard watches for that and
/// tells the UI layer, which tries to move focus to the overlay. It keeps reporting for as long as
/// the condition lasts, because an activation attempt can fail.
/// </summary>
internal sealed class ForegroundGuard
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(1);

    private readonly IForegroundProbe _probe;
    private readonly Action _onElevatedForeground;
    private readonly TimeSpan _interval;
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _thread;
    private volatile bool _stopping;

    public ForegroundGuard(IForegroundProbe probe, Action onElevatedForeground, TimeSpan? interval = null)
    {
        _probe = probe;
        _onElevatedForeground = onElevatedForeground;
        _interval = interval ?? TimeSpan.FromMilliseconds(500);
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Inactive foreground guard" };
        _thread.Start();
    }

    public void Stop()
    {
        _stopping = true;
        _wake.Set();
        _thread?.Join(JoinTimeout);
    }

    private void Run()
    {
        var logged = false;

        while (!_stopping)
        {
            if (_probe.IsForegroundElevated())
            {
                if (!logged)
                {
                    logged = true;
                    AppLog.Warn("An elevated window has focus, so input hooks may not see input.");
                }

                _onElevatedForeground();
            }

            _wake.WaitOne(_interval);
        }
    }
}
