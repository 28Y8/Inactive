using Inactive.Core.Input;
using Inactive.Core.Locking;
using Inactive.Core.Native;
using Inactive.Tests.TestSupport;

namespace Inactive.Tests.Locking;

public sealed class InputFilterTests
{
    private const int UnlockKey = VirtualKeys.RightShift;

    private readonly FakeClock _clock = new();
    private readonly RecordingSink _sink = new();
    private readonly PinnedPoint _pinnedPoint = new();
    private readonly HashSet<int> _downInWindows = [];
    private readonly LockStateMachine _machine;
    private readonly InputFilter _filter;
    private int _attempts;

    public InputFilterTests()
    {
        _machine = new LockStateMachine(UnlockKey, 4, _sink, _clock.Read);
        _machine.Begin(unlockKeyHeld: false);
        _filter = new InputFilter(_machine, _pinnedPoint, () => _attempts++, _clock.Read, _downInWindows.Contains);
    }

    private void PastGrace() => _clock.Advance(InputFilter.AttemptGraceMilliseconds);

    private bool KeyDown(int key) => _filter.OnKey(true, key, false);

    private bool KeyUp(int key) => _filter.OnKey(false, key, false);

    private bool Mouse(uint message, int x = 10, int y = 20, bool isInjected = false) =>
        _filter.OnMouse(message, x, y, isInjected);

    [Fact]
    public void Key_downs_are_swallowed()
    {
        Assert.True(KeyDown(VirtualKeys.A));
        Assert.True(KeyDown(VirtualKeys.Escape));
    }

    [Fact]
    public void A_release_with_no_press_seen_passes()
    {
        Assert.False(KeyUp(VirtualKeys.A));
        Assert.False(KeyUp(VirtualKeys.LeftControl));
        Assert.False(KeyUp(UnlockKey));
    }

    [Fact]
    public void A_key_held_since_before_the_lock_is_released_normally()
    {
        // The Ctrl of the hotkey: Windows saw it go down, so it must see it come up.
        _downInWindows.Add(VirtualKeys.LeftControl);

        Assert.True(KeyDown(VirtualKeys.LeftControl));
        Assert.True(KeyDown(VirtualKeys.LeftControl));

        Assert.False(KeyUp(VirtualKeys.LeftControl));
    }

    [Fact]
    public void The_release_of_a_swallowed_press_is_swallowed_too()
    {
        KeyDown(VirtualKeys.A);

        Assert.True(KeyUp(VirtualKeys.A));
    }

    [Fact]
    public void A_swallowed_release_is_swallowed_only_once()
    {
        KeyDown(VirtualKeys.A);
        KeyUp(VirtualKeys.A);

        Assert.False(KeyUp(VirtualKeys.A));
    }

    [Fact]
    public void Auto_repeat_still_ends_with_one_swallowed_release()
    {
        KeyDown(VirtualKeys.A);
        KeyDown(VirtualKeys.A);
        KeyDown(VirtualKeys.A);

        Assert.True(KeyUp(VirtualKeys.A));
        Assert.False(KeyUp(VirtualKeys.A));
    }

    [Fact]
    public void Pin_digits_never_reach_the_foreground_app()
    {
        KeyDown(UnlockKey);

        Assert.True(KeyDown(VirtualKeys.D0 + 1));
        Assert.True(KeyDown(VirtualKeys.Backspace));
        Assert.True(KeyDown(VirtualKeys.Enter));
    }

    [Fact]
    public void Pin_digit_releases_never_reach_the_foreground_app()
    {
        KeyDown(UnlockKey);
        KeyUp(UnlockKey);

        KeyDown(VirtualKeys.D0 + 1);

        Assert.True(KeyUp(VirtualKeys.D0 + 1));
    }

    [Fact]
    public void A_press_that_passes_after_unlocking_keeps_its_release()
    {
        // Swallowing this release while Windows holds the key down would leave the key stuck.
        KeyDown(VirtualKeys.A);
        _machine.ForceUnlock(UnlockReason.Exit);

        Assert.False(KeyDown(VirtualKeys.A));
        Assert.False(KeyUp(VirtualKeys.A));
    }

    [Fact]
    public void A_key_outside_the_known_range_is_harmless()
    {
        Assert.True(KeyDown(300));
        Assert.False(KeyUp(300));
    }

    [Fact]
    public void Unlock_key_that_ends_the_lock_is_swallowed_too()
    {
        var machine = new LockStateMachine(UnlockKey, 0, _sink, _clock.Read);
        machine.Begin(false);
        var filter = new InputFilter(machine, _pinnedPoint, () => { }, _clock.Read, _downInWindows.Contains);

        Assert.True(filter.OnKey(true, UnlockKey, false));
        Assert.Equal(LockPhase.Unlocked, machine.Phase);

        // The hooks are still on for the drain, and its release must stay out of sight as well.
        Assert.True(filter.OnKey(false, UnlockKey, false));
    }

    [Fact]
    public void Repeats_of_the_last_digit_stay_out_of_the_foreground_app()
    {
        KeyDown(UnlockKey);
        KeyUp(UnlockKey);
        KeyDown(VirtualKeys.D0);
        KeyDown(VirtualKeys.D0 + 1);
        KeyDown(VirtualKeys.D0 + 2);
        KeyDown(VirtualKeys.D0 + 3);

        _machine.CompleteVerification(_sink.LastToken, accepted: true);

        Assert.Equal(LockPhase.Unlocked, _machine.Phase);
        Assert.True(KeyDown(VirtualKeys.D0 + 3));
        Assert.True(KeyUp(VirtualKeys.D0 + 3));
    }

    [Fact]
    public void Enter_stays_out_of_the_foreground_app_during_the_drain()
    {
        KeyDown(UnlockKey);
        KeyUp(UnlockKey);
        KeyDown(VirtualKeys.D0);
        KeyDown(VirtualKeys.D0 + 1);
        KeyDown(VirtualKeys.D0 + 2);
        KeyDown(VirtualKeys.D0 + 3);
        _machine.CompleteVerification(_sink.LastToken, accepted: true);

        Assert.True(KeyDown(VirtualKeys.Enter));
        Assert.True(KeyUp(VirtualKeys.Enter));
    }

    [Fact]
    public void Other_keys_and_the_mouse_pass_during_the_drain()
    {
        var machine = new LockStateMachine(UnlockKey, 0, _sink, _clock.Read);
        machine.Begin(false);
        var filter = new InputFilter(machine, _pinnedPoint, () => { }, _clock.Read, _downInWindows.Contains);
        filter.OnKey(true, UnlockKey, false);

        Assert.False(filter.OnKey(true, VirtualKeys.A, false));
        Assert.False(filter.OnKey(false, VirtualKeys.A, false));
        Assert.False(filter.OnMouse(NativeConstants.WM_LBUTTONDOWN, 1, 2, false));
    }

    [Fact]
    public void Everything_passes_once_unlocked()
    {
        _machine.ForceUnlock(UnlockReason.Exit);

        Assert.False(KeyDown(VirtualKeys.A));
        Assert.False(Mouse(NativeConstants.WM_MOUSEMOVE));
        Assert.False(Mouse(NativeConstants.WM_LBUTTONDOWN));
    }

    [Fact]
    public void Injected_keys_are_treated_like_physical_ones()
    {
        // The filter has no injected parameter for keys at all: this pins that design down.
        var keyMethod = typeof(InputFilter).GetMethod(nameof(InputFilter.OnKey))!;

        Assert.DoesNotContain(keyMethod.GetParameters(), p => p.Name!.Contains("inject", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(NativeConstants.WM_MOUSEMOVE)]
    [InlineData(NativeConstants.WM_LBUTTONDOWN)]
    [InlineData(NativeConstants.WM_RBUTTONDOWN)]
    [InlineData(NativeConstants.WM_MBUTTONDOWN)]
    [InlineData(NativeConstants.WM_XBUTTONDOWN)]
    [InlineData(NativeConstants.WM_MOUSEWHEEL)]
    [InlineData(NativeConstants.WM_MOUSEHWHEEL)]
    public void Mouse_movement_wheel_and_presses_are_swallowed(uint message) => Assert.True(Mouse(message));

    [Theory]
    [InlineData(NativeConstants.WM_LBUTTONUP)]
    [InlineData(NativeConstants.WM_RBUTTONUP)]
    [InlineData(NativeConstants.WM_MBUTTONUP)]
    [InlineData(NativeConstants.WM_XBUTTONUP)]
    public void Mouse_button_ups_always_pass(uint message) => Assert.False(Mouse(message));

    [Fact]
    public void Cursor_guard_move_to_the_pinned_point_passes_quietly()
    {
        PastGrace();
        _pinnedPoint.Set(960, 540);

        Assert.False(Mouse(NativeConstants.WM_MOUSEMOVE, 960, 540, isInjected: true));

        Assert.Equal(0, _attempts);
        Assert.Equal(0, _filter.MouseEventsSwallowed);
    }

    [Fact]
    public void Injected_move_elsewhere_is_swallowed()
    {
        _pinnedPoint.Set(960, 540);

        Assert.True(Mouse(NativeConstants.WM_MOUSEMOVE, 100, 100, isInjected: true));
    }

    [Fact]
    public void Physical_move_on_the_pinned_point_is_still_swallowed()
    {
        _pinnedPoint.Set(960, 540);

        Assert.True(Mouse(NativeConstants.WM_MOUSEMOVE, 960, 540, isInjected: false));
    }

    [Fact]
    public void Injected_click_on_the_pinned_point_is_still_swallowed()
    {
        _pinnedPoint.Set(960, 540);

        Assert.True(Mouse(NativeConstants.WM_LBUTTONDOWN, 960, 540, isInjected: true));
    }

    [Fact]
    public void Injected_move_before_a_point_is_published_is_swallowed() =>
        Assert.True(Mouse(NativeConstants.WM_MOUSEMOVE, 0, 0, isInjected: true));

    [Fact]
    public void Swallowed_events_are_counted_by_hook()
    {
        KeyDown(VirtualKeys.A);
        KeyDown(VirtualKeys.Z);
        KeyUp(VirtualKeys.A);
        Mouse(NativeConstants.WM_MOUSEMOVE);
        Mouse(NativeConstants.WM_LBUTTONUP);

        Assert.Equal(3, _filter.KeyboardEventsSwallowed);
        Assert.Equal(1, _filter.MouseEventsSwallowed);
    }

    [Fact]
    public void Attempt_is_signalled_for_a_key_press()
    {
        PastGrace();

        KeyDown(VirtualKeys.A);

        Assert.Equal(1, _attempts);
    }

    [Fact]
    public void Attempt_is_signalled_for_mouse_input()
    {
        PastGrace();

        Mouse(NativeConstants.WM_MOUSEMOVE);

        Assert.Equal(1, _attempts);
    }

    [Fact]
    public void Attempts_are_throttled_to_one_every_two_seconds()
    {
        PastGrace();

        KeyDown(VirtualKeys.A);
        KeyUp(VirtualKeys.A);
        KeyDown(VirtualKeys.A);
        Mouse(NativeConstants.WM_MOUSEMOVE);
        _clock.Advance(InputFilter.AttemptThrottleMilliseconds - 1);
        Mouse(NativeConstants.WM_MOUSEMOVE);
        Assert.Equal(1, _attempts);

        _clock.Advance(1);
        Mouse(NativeConstants.WM_MOUSEMOVE);
        Assert.Equal(2, _attempts);
    }

    [Fact]
    public void Nothing_is_signalled_right_after_locking()
    {
        KeyDown(VirtualKeys.L);
        Mouse(NativeConstants.WM_MOUSEMOVE);
        Assert.Equal(0, _attempts);

        PastGrace();
        Mouse(NativeConstants.WM_MOUSEMOVE);
        Assert.Equal(1, _attempts);
    }

    [Fact]
    public void Auto_repeat_is_not_an_attempt()
    {
        KeyDown(VirtualKeys.A);
        PastGrace();

        KeyDown(VirtualKeys.A);

        Assert.Equal(0, _attempts);
    }

    [Fact]
    public void Typing_a_pin_is_not_an_attempt()
    {
        PastGrace();
        KeyDown(UnlockKey);

        KeyDown(VirtualKeys.D0);
        KeyDown(VirtualKeys.A);
        Mouse(NativeConstants.WM_MOUSEMOVE);

        Assert.Equal(0, _attempts);
    }

    [Fact]
    public void Mouse_button_up_is_not_an_attempt()
    {
        PastGrace();

        Mouse(NativeConstants.WM_LBUTTONUP);

        Assert.Equal(0, _attempts);
    }

    [Fact]
    public void Input_during_a_cooldown_is_an_attempt()
    {
        for (var i = 0; i < LockStateMachine.WrongAttemptsBeforeCooldown; i++)
        {
            if (_machine.Phase == LockPhase.Locked)
            {
                KeyDown(UnlockKey);
                KeyUp(UnlockKey);
            }

            foreach (var key in new[] { VirtualKeys.D0, VirtualKeys.D0, VirtualKeys.D0, VirtualKeys.D0 })
            {
                KeyDown(key);
                KeyUp(key);
            }

            _machine.CompleteVerification(_sink.LastToken, accepted: false);
        }

        Assert.Equal(LockPhase.CoolingDown, _machine.Phase);
        PastGrace();

        KeyDown(VirtualKeys.A);

        Assert.Equal(1, _attempts);
    }
}
