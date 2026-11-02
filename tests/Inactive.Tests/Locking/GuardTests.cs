using Inactive.Core.Locking;
using Inactive.Core.Native;
using Inactive.Core.Power;
using Inactive.Core.Security;
using Inactive.Tests.TestSupport;

namespace Inactive.Tests.Locking;

public sealed class CursorGuardTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(1);

    private readonly FakeCursorApi _api = new();
    private readonly PinnedPoint _pinnedPoint = new();

    [Fact]
    public void Pins_the_cursor_to_the_primary_centre_and_keeps_reasserting()
    {
        var guard = new CursorGuard(_api, _pinnedPoint, Fast);

        guard.Start();

        Assert.True(Wait.Until(() => _api.SetPositionCalls >= 3));
        Assert.Equal((960, 540), _api.LastPosition);
        Assert.True(_pinnedPoint.IsAt(960, 540));

        guard.Stop();
    }

    [Fact]
    public void Picks_up_a_display_change()
    {
        var guard = new CursorGuard(_api, _pinnedPoint, Fast);
        guard.Start();
        Assert.True(Wait.Until(() => _api.SetPositionCalls > 0));

        _api.Width = 3840;
        _api.Height = 2160;

        Assert.True(Wait.Until(() => _pinnedPoint.IsAt(1920, 1080)));
        guard.Stop();
    }

    [Fact]
    public void Stop_ends_the_loop()
    {
        var guard = new CursorGuard(_api, _pinnedPoint, Fast);
        guard.Start();
        Assert.True(Wait.Until(() => _api.SetPositionCalls > 0));

        guard.Stop();
        var callsAfterStop = _api.SetPositionCalls;
        Thread.Sleep(20);

        Assert.Equal(callsAfterStop, _api.SetPositionCalls);
    }

    [Fact]
    public void Reassert_does_not_wait_for_the_interval()
    {
        var guard = new CursorGuard(_api, _pinnedPoint, TimeSpan.FromHours(1));
        guard.Start();
        Assert.True(Wait.Until(() => _api.SetPositionCalls == 1));

        guard.Reassert();

        Assert.True(Wait.Until(() => _api.SetPositionCalls == 2));
        guard.Stop();
    }

    [Fact]
    public void Stop_without_start_is_harmless()
    {
        var guard = new CursorGuard(_api, _pinnedPoint);

        guard.Stop();

        Assert.Equal(0, _api.SetPositionCalls);
    }
}

public sealed class ElevatedFocusWatchTests
{
    private const long Start = 10_000;
    private const long Report = 500;

    private readonly ElevatedFocusWatch _watch = new();

    private long ReportFor(long milliseconds)
    {
        var now = Start;
        for (; now <= Start + milliseconds; now += Report)
        {
            _watch.OnReport(now);
        }

        return now - Report;
    }

    [Fact]
    public void Says_nothing_before_any_report() => Assert.False(_watch.ShouldShowHint(Start));

    [Fact]
    public void A_short_visit_shows_no_hint()
    {
        var now = ReportFor(ElevatedFocusWatch.HintDelayMilliseconds - Report);

        Assert.False(_watch.ShouldShowHint(now));
    }

    [Fact]
    public void Lasting_focus_shows_the_hint()
    {
        var now = ReportFor(ElevatedFocusWatch.HintDelayMilliseconds);

        Assert.True(_watch.ShouldShowHint(now));
    }

    [Fact]
    public void The_hint_goes_when_the_reports_stop()
    {
        var now = ReportFor(ElevatedFocusWatch.HintDelayMilliseconds);

        Assert.False(_watch.ShouldShowHint(now + 2_000));
    }

    [Fact]
    public void A_new_visit_starts_the_wait_again()
    {
        var now = ReportFor(ElevatedFocusWatch.HintDelayMilliseconds);

        _watch.OnReport(now + 10_000);

        Assert.False(_watch.ShouldShowHint(now + 10_000));
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var now = ReportFor(ElevatedFocusWatch.HintDelayMilliseconds);

        _watch.Reset();

        Assert.False(_watch.ShouldShowHint(now));
    }
}

public sealed class ForegroundGuardTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(1);

    private readonly FakeForegroundProbe _probe = new();
    private int _reports;

    private void Report() => Interlocked.Increment(ref _reports);

    [Fact]
    public void Stays_quiet_for_a_normal_foreground_window()
    {
        var guard = new ForegroundGuard(_probe, Report, Fast);

        guard.Start();
        Thread.Sleep(20);
        guard.Stop();

        Assert.Equal(0, Volatile.Read(ref _reports));
    }

    [Fact]
    public void Keeps_reporting_while_an_elevated_window_has_focus()
    {
        _probe.Elevated = true;
        var guard = new ForegroundGuard(_probe, Report, Fast);

        guard.Start();

        Assert.True(Wait.Until(() => Volatile.Read(ref _reports) >= 3));
        guard.Stop();
    }

    [Fact]
    public void Stop_ends_the_reports()
    {
        _probe.Elevated = true;
        var guard = new ForegroundGuard(_probe, Report, Fast);
        guard.Start();
        Assert.True(Wait.Until(() => Volatile.Read(ref _reports) > 0));

        guard.Stop();
        var reportsAfterStop = Volatile.Read(ref _reports);
        Thread.Sleep(20);

        Assert.Equal(reportsAfterStop, Volatile.Read(ref _reports));
    }
}

public sealed class KeepAwakeTests : IDisposable
{
    private const uint SystemAndDisplay =
        NativeConstants.ES_CONTINUOUS | NativeConstants.ES_SYSTEM_REQUIRED | NativeConstants.ES_DISPLAY_REQUIRED;

    private const uint SystemOnly = NativeConstants.ES_CONTINUOUS | NativeConstants.ES_SYSTEM_REQUIRED;

    private readonly FakeExecutionStateApi _api = new();
    private readonly KeepAwake _keepAwake;

    public KeepAwakeTests() => _keepAwake = new KeepAwake(_api, TimeSpan.FromHours(1));

    public void Dispose() => _keepAwake.Dispose();

    [Fact]
    public void Acquire_keeps_system_and_display_awake()
    {
        Assert.True(_keepAwake.Acquire(allowDisplayOff: false));

        Assert.Equal(SystemAndDisplay, _api.Calls[^1].Flags);
    }

    [Fact]
    public void Allowing_display_off_drops_the_display_flag()
    {
        _keepAwake.Acquire(allowDisplayOff: true);

        Assert.Equal(SystemOnly, _api.Calls[^1].Flags);
    }

    [Fact]
    public void Release_clears_with_continuous_only()
    {
        _keepAwake.Acquire(allowDisplayOff: false);

        _keepAwake.Release();

        Assert.Equal(NativeConstants.ES_CONTINUOUS, _api.Calls[^1].Flags);
    }

    [Fact]
    public void Every_call_comes_from_one_thread_that_is_not_the_callers()
    {
        _keepAwake.Acquire(allowDisplayOff: false);
        _keepAwake.Release();
        _keepAwake.Acquire(allowDisplayOff: true);
        _keepAwake.Release();

        var threads = _api.Calls.Select(call => call.ThreadId).Distinct().ToArray();

        Assert.Single(threads);
        Assert.NotEqual(Environment.CurrentManagedThreadId, threads[0]);
    }

    [Fact]
    public void Reports_when_windows_refuses()
    {
        _api.Result = 0;

        Assert.False(_keepAwake.Acquire(allowDisplayOff: false));
    }

    [Fact]
    public void Reapplies_on_the_interval_while_held()
    {
        using var keepAwake = new KeepAwake(_api, TimeSpan.FromMilliseconds(1));

        keepAwake.Acquire(allowDisplayOff: false);

        Assert.True(Wait.Until(() => _api.Calls.Count >= 4));
        Assert.All(_api.Calls, call => Assert.Equal(SystemAndDisplay, call.Flags));
    }

    [Fact]
    public void Does_not_reapply_after_release()
    {
        using var keepAwake = new KeepAwake(_api, TimeSpan.FromMilliseconds(1));
        keepAwake.Acquire(allowDisplayOff: false);
        keepAwake.Release();

        var calls = _api.Calls.Count;
        Thread.Sleep(20);

        Assert.Equal(calls, _api.Calls.Count);
    }

    [Fact]
    public void Reassert_applies_right_away()
    {
        _keepAwake.Acquire(allowDisplayOff: false);
        var calls = _api.Calls.Count;

        _keepAwake.Reassert();

        Assert.True(Wait.Until(() => _api.Calls.Count > calls));
    }

    [Fact]
    public void Dispose_clears_the_state_on_its_way_out()
    {
        _keepAwake.Acquire(allowDisplayOff: false);

        _keepAwake.Dispose();

        Assert.Equal(NativeConstants.ES_CONTINUOUS, _api.Calls[^1].Flags);
    }

    [Fact]
    public void Calls_after_dispose_are_harmless()
    {
        _keepAwake.Dispose();

        _keepAwake.Release();
        _keepAwake.Reassert();
        _keepAwake.Dispose();

        Assert.False(_keepAwake.Acquire(allowDisplayOff: false));
    }
}

public sealed class PinVerifierTests
{
    private readonly PinCredential _credential = PinHasher.Create("1234", 1_000);
    private readonly ManualResetEventSlim _done = new();
    private long _token;
    private bool _accepted;

    private void OnResult(long token, bool accepted)
    {
        _token = token;
        _accepted = accepted;
        _done.Set();
    }

    [Fact]
    public void Reports_a_right_pin_with_its_token()
    {
        var verifier = new PinVerifier(_credential, OnResult);
        verifier.Start();

        verifier.Submit("1234", 7);

        Assert.True(Wait.For(_done));
        Assert.Equal(7, _token);
        Assert.True(_accepted);
        verifier.Stop();
    }

    [Fact]
    public void Reports_a_wrong_pin()
    {
        var verifier = new PinVerifier(_credential, OnResult);
        verifier.Start();

        verifier.Submit("9999", 8);

        Assert.True(Wait.For(_done));
        Assert.False(_accepted);
        verifier.Stop();
    }

    [Fact]
    public void Verifies_off_the_calling_thread()
    {
        var thread = 0;
        var verifier = new PinVerifier(_credential, (_, _) =>
        {
            thread = Environment.CurrentManagedThreadId;
            _done.Set();
        });
        verifier.Start();

        verifier.Submit("1234", 1);

        Assert.True(Wait.For(_done));
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread);
        verifier.Stop();
    }

    [Fact]
    public void Submit_after_stop_is_dropped()
    {
        var verifier = new PinVerifier(_credential, OnResult);
        verifier.Start();

        verifier.Stop();
        verifier.Submit("1234", 1);

        Assert.False(Wait.For(_done, TimeSpan.FromMilliseconds(50)));
    }
}

public sealed class EngineEventQueueTests
{
    private static EngineEvent Number(int value) => new(EngineEventKind.PinProgress, value);

    [Fact]
    public void Delivers_in_order_on_one_background_thread()
    {
        var seen = new List<(int Value, int Thread)>();
        using var finished = new ManualResetEventSlim();
        var queue = new EngineEventQueue(e =>
        {
            seen.Add((e.First, Environment.CurrentManagedThreadId));
            if (e.First == 49)
            {
                finished.Set();
            }
        });
        queue.EnsureStarted();

        for (var i = 0; i < 50; i++)
        {
            queue.Post(Number(i));
        }

        Assert.True(Wait.For(finished));
        queue.Stop();

        Assert.Equal(Enumerable.Range(0, 50), seen.Select(item => item.Value));
        Assert.Single(seen.Select(item => item.Thread).Distinct());
        Assert.NotEqual(Environment.CurrentManagedThreadId, seen[0].Thread);
    }

    [Fact]
    public void A_full_queue_drops_the_oldest_and_keeps_the_newest()
    {
        var seen = new List<int>();
        using var gate = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var queue = new EngineEventQueue(
            e =>
            {
                if (e.First == -1)
                {
                    entered.Set();
                    gate.Wait();
                    return;
                }

                seen.Add(e.First);
            },
            capacity: 4);
        queue.EnsureStarted();

        queue.Post(Number(-1));
        Assert.True(Wait.For(entered));

        for (var i = 0; i < 10; i++)
        {
            queue.Post(Number(i));
        }

        gate.Set();
        queue.Stop();

        Assert.Equal([6, 7, 8, 9], seen);
    }

    [Fact]
    public void Post_never_waits_for_a_stuck_subscriber()
    {
        using var gate = new ManualResetEventSlim();
        var queue = new EngineEventQueue(_ => gate.Wait(), capacity: 4);
        queue.EnsureStarted();

        for (var i = 0; i < 1_000; i++)
        {
            queue.Post(Number(i));
        }

        gate.Set();
        queue.Stop();
    }

    [Fact]
    public void A_throwing_subscriber_does_not_stop_delivery()
    {
        using var finished = new ManualResetEventSlim();
        var queue = new EngineEventQueue(e =>
        {
            if (e.First == 0)
            {
                throw new InvalidOperationException("Subscriber bug");
            }

            finished.Set();
        });
        queue.EnsureStarted();

        queue.Post(Number(0));
        queue.Post(Number(1));

        Assert.True(Wait.For(finished));
        queue.Stop();
    }

    [Fact]
    public void Stop_delivers_what_is_already_queued()
    {
        var seen = new List<int>();
        var queue = new EngineEventQueue(e => seen.Add(e.First));
        queue.EnsureStarted();

        queue.Post(Number(1));
        queue.Post(Number(2));
        queue.Stop();

        Assert.Equal([1, 2], seen);
    }
}
