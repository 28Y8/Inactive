namespace Inactive.Core.Locking;

/// <summary>
/// Turns the repeating <see cref="LockController.ElevatedForeground"/> event into a yes or no: has an
/// elevated window kept focus long enough that the person needs to be told about the way out? The
/// event only says that the condition holds, so reports that stop mean it has ended. Taking focus
/// succeeded or the window closed, and either way the unlock key works again.
/// </summary>
public sealed class ElevatedFocusWatch
{
    /// <summary>How long focus has to stay on the elevated window before the hint shows.</summary>
    public const long HintDelayMilliseconds = 2_000;

    // The event repeats every 500 ms. This allows for a few late ones on a busy UI thread.
    private const long ReportGapMilliseconds = 1_250;

    private long? _since;
    private long _lastReport;

    /// <summary>Records one report of the event.</summary>
    /// <param name="now">A monotonic time in milliseconds.</param>
    public void OnReport(long now)
    {
        if (_since is null || now - _lastReport > ReportGapMilliseconds)
        {
            _since = now;
        }

        _lastReport = now;
    }

    /// <summary>True when the hint should be on screen right now.</summary>
    /// <param name="now">A monotonic time in milliseconds.</param>
    public bool ShouldShowHint(long now) =>
        _since is { } since
        && now - _lastReport <= ReportGapMilliseconds
        && now - since >= HintDelayMilliseconds;

    public void Reset() => _since = null;
}
