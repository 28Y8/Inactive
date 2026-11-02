using System.Collections.Concurrent;
using Inactive.Core.Input;
using Inactive.Core.Locking;
using Inactive.Core.Native;
using Inactive.Core.Security;
using Inactive.Tests.TestSupport;

namespace Inactive.Tests.Locking;

public sealed class LockControllerTests : IDisposable
{
    private const int UnlockKey = VirtualKeys.RightShift;

    private readonly FakeLockPlatform _platform = new();
    private readonly LockController _controller;
    private readonly ConcurrentQueue<string> _events = new();
    private readonly ManualResetEventSlim _unlocked = new();
    private readonly ConcurrentQueue<int> _eventThreads = new();

    public LockControllerTests()
    {
        _controller = new LockController(_platform);
        Record(_controller);
    }

    public void Dispose() => _controller.Dispose();

    private void Record(LockController controller)
    {
        void Add(string text)
        {
            _eventThreads.Enqueue(Environment.CurrentManagedThreadId);
            _events.Enqueue(text);
        }

        controller.Locked += (_, _) => Add("locked");
        controller.Unlocked += (_, e) =>
        {
            Add($"unlocked:{e.Reason}");
            _unlocked.Set();
        };
        controller.PhaseChanged += (_, e) => Add($"phase:{e.Phase}");
        controller.PinProgress += (_, e) => Add($"progress:{e.Entered}/{e.Length}");
        controller.PinRejected += (_, _) => Add("rejected");
        controller.InputAttempted += (_, _) => Add("attempted");
        controller.ElevatedForeground += (_, _) => Add("elevated");
    }

    private bool Saw(string text) => Wait.Until(() => _events.Contains(text));

    private void Press(int key)
    {
        _platform.Blocker.Filter!.OnKey(true, key, false);
        _platform.Blocker.Filter!.OnKey(false, key, false);
    }

    private void TypePin(string digits)
    {
        foreach (var digit in digits)
        {
            Press(VirtualKeys.D0 + (digit - '0'));
        }
    }

    private static LockRequest WithPin(string pin) =>
        new(UnlockKey, PinHasher.Create(pin, 1_000));

    [Fact]
    public void Starts_unlocked()
    {
        Assert.Equal(LockPhase.Unlocked, _controller.State);
        Assert.False(_controller.IsLocked);
    }

    [Fact]
    public void Creating_a_controller_engages_nothing()
    {
        Assert.Null(_platform.Blocker.Filter);
        Assert.Empty(_platform.FakeExecutionState.Calls);
        Assert.Equal(0, _platform.FakeCursor.SetPositionCalls);
    }

    [Fact]
    public void TryLock_engages_everything()
    {
        var result = _controller.TryLock(new LockRequest(UnlockKey));

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Message);
        Assert.True(_controller.IsLocked);
        Assert.Equal(LockPhase.Locked, _controller.State);
        Assert.True(Wait.Until(() => _platform.FakeCursor.SetPositionCalls > 0));
        Assert.Equal(
            NativeConstants.ES_CONTINUOUS | NativeConstants.ES_SYSTEM_REQUIRED | NativeConstants.ES_DISPLAY_REQUIRED,
            _platform.FakeExecutionState.Calls[^1].Flags);
    }

    [Fact]
    public void Allow_display_off_reaches_keep_awake()
    {
        _controller.TryLock(new LockRequest(UnlockKey, AllowDisplayOff: true));

        Assert.Equal(
            NativeConstants.ES_CONTINUOUS | NativeConstants.ES_SYSTEM_REQUIRED,
            _platform.FakeExecutionState.Calls[^1].Flags);
    }

    [Fact]
    public void Locked_is_raised_first_and_followed_by_the_phase()
    {
        _controller.TryLock(new LockRequest(UnlockKey));

        Assert.True(Saw("phase:Locked"));
        Assert.Equal(["locked", "phase:Locked"], _events);
    }

    [Fact]
    public void A_second_lock_is_refused()
    {
        _controller.TryLock(new LockRequest(UnlockKey));

        var result = _controller.TryLock(new LockRequest(UnlockKey));

        Assert.False(result.Succeeded);
        Assert.Equal(LockError.AlreadyLocked, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(VirtualKeys.Enter)]
    [InlineData(VirtualKeys.Shift)]
    [InlineData(300)]
    public void An_unusable_unlock_key_is_refused_before_anything_engages(int key)
    {
        var result = _controller.TryLock(new LockRequest(key));

        Assert.Equal(LockError.InvalidUnlockKey, result.Error);
        Assert.NotEqual(string.Empty, result.Message);
        Assert.False(_controller.IsLocked);
        Assert.Empty(_platform.FakeExecutionState.Calls);
        Assert.Null(_platform.Blocker.Filter);
    }

    [Fact]
    public void A_damaged_pin_is_refused_before_anything_engages()
    {
        var pin = new PinCredential("not base64!", "AAAA", 200_000, 4);

        var result = _controller.TryLock(new LockRequest(UnlockKey, pin));

        Assert.Equal(LockError.InvalidPin, result.Error);
        Assert.False(_controller.IsLocked);
        Assert.Null(_platform.Blocker.Filter);
    }

    [Fact]
    public void Hook_failure_aborts_cleanly()
    {
        _platform.Blocker = new FakeInputBlocker { StartResult = new HookStartResult(false, 1428) };

        var result = _controller.TryLock(new LockRequest(UnlockKey));

        Assert.False(result.Succeeded);
        Assert.Equal(LockError.HookInstallFailed, result.Error);
        Assert.Equal(1428, result.Win32Error);
        Assert.NotEqual(string.Empty, result.Message);
        Assert.False(_controller.IsLocked);
        AssertCursorGuardStopped();
        Assert.Equal(NativeConstants.ES_CONTINUOUS, _platform.FakeExecutionState.Calls[^1].Flags);
        Assert.Empty(_events);
    }

    [Fact]
    public void Locking_works_again_after_a_hook_failure()
    {
        _platform.Blocker = new FakeInputBlocker { StartResult = new HookStartResult(false, 5) };
        _controller.TryLock(new LockRequest(UnlockKey));

        _platform.Blocker = new FakeInputBlocker();

        Assert.True(_controller.TryLock(new LockRequest(UnlockKey)).Succeeded);
    }

    [Fact]
    public void Unlock_key_releases_everything_without_any_outside_help()
    {
        _controller.TryLock(new LockRequest(UnlockKey));

        Press(UnlockKey);

        Assert.True(Wait.For(_unlocked));
        Assert.False(_controller.IsLocked);
        AssertCursorGuardStopped();
        Assert.Equal(NativeConstants.ES_CONTINUOUS, _platform.FakeExecutionState.Calls[^1].Flags);
        Assert.Contains("unlocked:UnlockKey", _events);
    }

    [Fact]
    public void A_hung_subscriber_cannot_hold_up_the_release()
    {
        var gate = new ManualResetEventSlim();
        _controller.Locked += (_, _) => gate.Wait();
        _controller.TryLock(new LockRequest(UnlockKey));

        try
        {
            Press(UnlockKey);

            Assert.True(Wait.Until(() =>
                _platform.FakeExecutionState.Calls[^1].Flags == NativeConstants.ES_CONTINUOUS));

            // Delivery is stuck on the second Locked handler, yet the engine has let go.
            Assert.Equal(["locked"], _events);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public void Unlock_key_held_at_lock_time_must_be_released_first()
    {
        _platform.HoldKey(UnlockKey);
        _controller.TryLock(new LockRequest(UnlockKey));

        _platform.Blocker.Filter!.OnKey(true, UnlockKey, false);
        Assert.True(_controller.IsLocked);

        _platform.Blocker.Filter!.OnKey(false, UnlockKey, false);
        _platform.Blocker.Filter!.OnKey(true, UnlockKey, false);
        _platform.Blocker.Filter!.OnKey(false, UnlockKey, false);

        Assert.True(Wait.For(_unlocked));
    }

    [Fact]
    public void The_hooks_stay_on_until_the_unlock_key_comes_up()
    {
        _controller.TryLock(new LockRequest(UnlockKey));

        _platform.Blocker.Filter!.OnKey(true, UnlockKey, false);

        Assert.False(_controller.IsLocked);
        Assert.False(Wait.For(_unlocked, TimeSpan.FromMilliseconds(50)));

        _platform.Blocker.Filter!.OnKey(false, UnlockKey, false);

        Assert.True(Wait.For(_unlocked));
        Assert.Contains("unlocked:UnlockKey", _events);
    }

    [Fact]
    public void A_key_that_stays_down_cannot_hold_the_hooks_on()
    {
        _controller.TryLock(new LockRequest(UnlockKey));
        _platform.Blocker.Filter!.OnKey(true, UnlockKey, false);

        _platform.Advance(LockStateMachine.DrainMilliseconds);
        _platform.Blocker.Host!.OnTick();

        Assert.True(Wait.For(_unlocked));
    }

    [Fact]
    public void Release_does_not_wait_for_the_drain()
    {
        _controller.TryLock(new LockRequest(UnlockKey));
        _platform.Blocker.Filter!.OnKey(true, UnlockKey, false);

        Assert.True(_controller.Release(UnlockReason.Exit));

        Assert.True(Wait.For(_unlocked));
    }

    [Fact]
    public void Right_pin_unlocks_through_the_real_verifier()
    {
        _controller.TryLock(WithPin("4321"));

        Press(UnlockKey);
        TypePin("4321");

        Assert.True(Wait.For(_unlocked));
        Assert.Contains("unlocked:Pin", _events);
        Assert.Contains("progress:4/4", _events);
    }

    [Fact]
    public void Wrong_pin_is_rejected_through_the_real_verifier()
    {
        _controller.TryLock(WithPin("4321"));

        Press(UnlockKey);
        TypePin("1111");

        Assert.True(Saw("rejected"));
        Assert.Equal(LockPhase.PinEntry, _controller.State);
    }

    [Fact]
    public void Pin_events_carry_counts_only()
    {
        _controller.TryLock(WithPin("4321"));

        Press(UnlockKey);
        TypePin("4321");

        Assert.True(Wait.For(_unlocked));
        Assert.DoesNotContain(_events, text => text.Contains("4321", StringComparison.Ordinal));
    }

    [Fact]
    public void Five_wrong_pins_show_the_cooldown_on_the_controller()
    {
        _controller.TryLock(WithPin("4321"));
        Press(UnlockKey);

        for (var attempt = 1; attempt <= LockStateMachine.WrongAttemptsBeforeCooldown; attempt++)
        {
            var expected = attempt;
            TypePin("1111");
            Assert.True(Wait.Until(() => _events.Count(text => text == "rejected") == expected));
            Assert.True(Wait.Until(() => _controller.State != LockPhase.VerifyingPin));
        }

        Assert.Equal(LockPhase.CoolingDown, _controller.State);
        Assert.Equal(TimeSpan.FromSeconds(30), _controller.CooldownRemaining);
    }

    [Fact]
    public void Tick_from_the_hook_thread_applies_timeouts()
    {
        _controller.TryLock(WithPin("4321"));
        Press(UnlockKey);
        Assert.Equal(LockPhase.PinEntry, _controller.State);

        _platform.Advance(LockStateMachine.PinIdleTimeoutMilliseconds);
        _platform.Blocker.Host!.OnTick();

        Assert.Equal(LockPhase.Locked, _controller.State);
    }

    [Fact]
    public void Input_attempts_reach_subscribers()
    {
        _controller.TryLock(new LockRequest(UnlockKey));
        _platform.Advance(InputFilter.AttemptGraceMilliseconds);

        _platform.Blocker.Filter!.OnMouse(NativeConstants.WM_MOUSEMOVE, 1, 2, false);

        Assert.True(Saw("attempted"));
    }

    [Fact]
    public void Elevated_foreground_reaches_subscribers()
    {
        _platform.FakeForeground.Elevated = true;

        _controller.TryLock(new LockRequest(UnlockKey));

        Assert.True(Saw("elevated"));
    }

    [Fact]
    public void Events_are_raised_off_the_calling_thread_on_one_thread()
    {
        _controller.TryLock(new LockRequest(UnlockKey));
        Press(UnlockKey);
        Assert.True(Wait.For(_unlocked));

        var threads = _eventThreads.Distinct().ToArray();

        Assert.Single(threads);
        Assert.NotEqual(Environment.CurrentManagedThreadId, threads[0]);
    }

    [Fact]
    public void Release_ends_the_lock_with_the_given_reason()
    {
        _controller.TryLock(new LockRequest(UnlockKey));

        Assert.True(_controller.Release(UnlockReason.SessionUnlocked));

        Assert.False(_controller.IsLocked);
        Assert.True(Saw("unlocked:SessionUnlocked"));
    }

    [Fact]
    public void Release_has_finished_the_teardown_when_it_returns()
    {
        _controller.TryLock(new LockRequest(UnlockKey));

        _controller.Release(UnlockReason.Shutdown);

        AssertCursorGuardStopped();
        Assert.Equal(NativeConstants.ES_CONTINUOUS, _platform.FakeExecutionState.Calls[^1].Flags);
    }

    [Fact]
    public void Release_works_during_pin_entry()
    {
        _controller.TryLock(WithPin("4321"));
        Press(UnlockKey);

        Assert.True(_controller.Release(UnlockReason.Exit));
        Assert.False(_controller.IsLocked);
    }

    [Fact]
    public void Release_when_unlocked_returns_false() =>
        Assert.False(_controller.Release(UnlockReason.Exit));

    [Fact]
    public void Release_gives_up_on_a_stuck_hook_thread()
    {
        _platform.Blocker = new FakeInputBlocker { HangOnStop = true };
        _controller.TryLock(new LockRequest(UnlockKey));

        Assert.True(_controller.Release(UnlockReason.Exit));
    }

    [Fact]
    public void Can_lock_again_after_unlocking()
    {
        _controller.TryLock(new LockRequest(UnlockKey));
        _controller.Release(UnlockReason.Exit);
        _platform.Blocker = new FakeInputBlocker();

        Assert.True(_controller.TryLock(new LockRequest(UnlockKey)).Succeeded);
        Assert.True(_controller.IsLocked);
    }

    [Fact]
    public void A_dead_hook_thread_fails_open()
    {
        _controller.TryLock(new LockRequest(UnlockKey));

        _platform.Blocker.RequestStop();

        Assert.True(Wait.For(_unlocked));
        Assert.Contains("unlocked:EngineFault", _events);
        Assert.False(_controller.IsLocked);
    }

    [Fact]
    public void Reassert_refreshes_the_hooks_and_keep_awake()
    {
        _controller.TryLock(new LockRequest(UnlockKey));
        var calls = _platform.FakeExecutionState.Calls.Count;

        _controller.Reassert();

        Assert.Equal(1, _platform.Blocker.ReinstallRequests);
        Assert.True(Wait.Until(() => _platform.FakeExecutionState.Calls.Count > calls));
    }

    [Fact]
    public void Reassert_when_unlocked_does_nothing()
    {
        _controller.Reassert();

        Assert.Equal(0, _platform.Blocker.ReinstallRequests);
        Assert.Empty(_platform.FakeExecutionState.Calls);
    }

    [Fact]
    public void Dispose_releases_a_held_lock_with_exit()
    {
        _controller.TryLock(new LockRequest(UnlockKey));

        _controller.Dispose();

        Assert.False(_controller.IsLocked);
        Assert.Contains("unlocked:Exit", _events);
    }

    [Fact]
    public void TryLock_after_dispose_throws()
    {
        _controller.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _controller.TryLock(new LockRequest(UnlockKey)));
    }

    [Fact]
    public void Request_from_settings_copies_the_lock_fields()
    {
        var pin = PinHasher.Create("1234", 1_000);
        var settings = new Core.Settings.AppSettings { UnlockKey = VirtualKeys.Pause, AllowDisplayOff = true }.WithPin(pin);

        var request = LockRequest.FromSettings(settings);

        Assert.Equal(new LockRequest(VirtualKeys.Pause, pin, true), request);
    }

    private void AssertCursorGuardStopped()
    {
        var calls = _platform.FakeCursor.SetPositionCalls;
        Thread.Sleep(120);
        Assert.Equal(calls, _platform.FakeCursor.SetPositionCalls);
    }
}
