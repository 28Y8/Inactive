using Inactive.Core.Input;
using Inactive.Core.Locking;
using Inactive.Tests.TestSupport;

namespace Inactive.Tests.Locking;

public sealed class LockStateMachineTests
{
    private const int UnlockKey = VirtualKeys.RightShift;
    private const int Digit0 = VirtualKeys.D0;

    private readonly FakeClock _clock = new();
    private readonly RecordingSink _sink = new();

    private LockStateMachine Create(int pinLength = 0, bool unlockKeyHeld = false)
    {
        var machine = new LockStateMachine(UnlockKey, pinLength, _sink, _clock.Read);
        machine.Begin(unlockKeyHeld);
        return machine;
    }

    private LockStateMachine CreateInPinEntry(int pinLength = 4)
    {
        var machine = Create(pinLength);
        Press(machine, UnlockKey);
        _sink.Events.Clear();
        return machine;
    }

    private static KeyDownResult Press(LockStateMachine machine, int key, bool isExtended = false)
    {
        var result = machine.OnKeyDown(key, isExtended);
        machine.OnKeyUp(key);
        return result;
    }

    private static void Type(LockStateMachine machine, string digits)
    {
        foreach (var digit in digits)
        {
            Press(machine, Digit0 + (digit - '0'));
        }
    }

    private void FailAttempts(LockStateMachine machine, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (machine.Phase == LockPhase.Locked)
            {
                Press(machine, UnlockKey);
            }

            Type(machine, "0000");
            machine.CompleteVerification(_sink.LastToken, accepted: false);
        }
    }

    // Construction

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(256)]
    public void Rejects_an_impossible_unlock_key(int key) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LockStateMachine(key, 0, _sink, _clock.Read));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public void Rejects_an_impossible_pin_length(int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LockStateMachine(UnlockKey, length, _sink, _clock.Read));

    [Fact]
    public void Starts_unlocked_and_lets_keys_through()
    {
        var machine = new LockStateMachine(UnlockKey, 0, _sink, _clock.Read);

        Assert.Equal(LockPhase.Unlocked, machine.Phase);
        Assert.Equal(KeyDownResult.NotLocked, machine.OnKeyDown(UnlockKey, false));
    }

    [Fact]
    public void Begin_enters_locked_silently()
    {
        var machine = Create();

        Assert.Equal(LockPhase.Locked, machine.Phase);
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void Begin_twice_throws()
    {
        var machine = Create();

        Assert.Throws<InvalidOperationException>(() => machine.Begin(false));
    }

    // Unlocking without a PIN

    [Fact]
    public void Unlock_key_unlocks_when_no_pin_is_set()
    {
        var machine = Create();

        var result = machine.OnKeyDown(UnlockKey, false);

        Assert.Equal(KeyDownResult.Consumed, result);
        Assert.Equal(LockPhase.Unlocked, machine.Phase);
        Assert.Equal(["phase:Unlocked", "unlocked:UnlockKey"], _sink.Events);
    }

    [Fact]
    public void Other_keys_are_blocked()
    {
        var machine = Create();

        Assert.Equal(KeyDownResult.Blocked, Press(machine, VirtualKeys.LeftShift));
        Assert.Equal(KeyDownResult.Blocked, Press(machine, VirtualKeys.Escape));
        Assert.Equal(KeyDownResult.Blocked, Press(machine, Digit0));
        Assert.Equal(LockPhase.Locked, machine.Phase);
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void Keys_after_unlocking_are_not_locked()
    {
        var machine = Create();
        Press(machine, UnlockKey);

        Assert.Equal(KeyDownResult.NotLocked, Press(machine, Digit0));
    }

    // Arming and auto repeat

    [Fact]
    public void Held_unlock_key_does_not_count_until_released()
    {
        var machine = Create(unlockKeyHeld: true);

        Assert.Equal(KeyDownResult.Repeat, machine.OnKeyDown(UnlockKey, false));
        _clock.Advance(100);
        Assert.Equal(KeyDownResult.Repeat, machine.OnKeyDown(UnlockKey, false));
        Assert.Equal(LockPhase.Locked, machine.Phase);

        machine.OnKeyUp(UnlockKey);
        machine.OnKeyDown(UnlockKey, false);

        Assert.Equal(LockPhase.Unlocked, machine.Phase);
    }

    [Fact]
    public void Long_auto_repeat_never_counts_as_a_press()
    {
        var machine = Create(unlockKeyHeld: true);

        for (var i = 0; i < 200; i++)
        {
            _clock.Advance(33);
            Assert.Equal(KeyDownResult.Repeat, machine.OnKeyDown(UnlockKey, false));
        }

        Assert.Equal(LockPhase.Locked, machine.Phase);
    }

    [Fact]
    public void Slowest_keyboard_delay_still_counts_as_a_repeat()
    {
        var machine = Create(unlockKeyHeld: true);
        _clock.Advance(1_000);

        Assert.Equal(KeyDownResult.Repeat, machine.OnKeyDown(UnlockKey, false));
    }

    [Fact]
    public void Lost_key_up_cannot_disable_the_unlock_key()
    {
        var machine = Create(unlockKeyHeld: true);

        _clock.Advance(LockStateMachine.RepeatWindowMilliseconds);
        machine.OnKeyDown(UnlockKey, false);

        Assert.Equal(LockPhase.Unlocked, machine.Phase);
    }

    [Fact]
    public void Repeat_of_another_key_is_reported_as_repeat()
    {
        var machine = Create();

        Assert.Equal(KeyDownResult.Blocked, machine.OnKeyDown(VirtualKeys.Space, false));
        Assert.Equal(KeyDownResult.Repeat, machine.OnKeyDown(VirtualKeys.Space, false));
    }

    [Fact]
    public void Unlock_key_held_through_pin_cancel_does_not_reopen_entry()
    {
        var machine = Create(pinLength: 4);

        machine.OnKeyDown(UnlockKey, false);
        Press(machine, VirtualKeys.Escape);
        Assert.Equal(LockPhase.Locked, machine.Phase);

        Assert.Equal(KeyDownResult.Repeat, machine.OnKeyDown(UnlockKey, false));
        Assert.Equal(LockPhase.Locked, machine.Phase);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    [InlineData(int.MaxValue)]
    public void Out_of_range_keys_are_harmless(int key)
    {
        var machine = Create();

        Assert.Equal(KeyDownResult.Blocked, machine.OnKeyDown(key, false));
        machine.OnKeyUp(key);
    }

    // PIN entry

    [Fact]
    public void Unlock_key_opens_pin_entry_when_a_pin_is_set()
    {
        var machine = Create(pinLength: 4);

        var result = machine.OnKeyDown(UnlockKey, false);

        Assert.Equal(KeyDownResult.Consumed, result);
        Assert.Equal(LockPhase.PinEntry, machine.Phase);
        Assert.Equal(["phase:PinEntry", "progress:0/4"], _sink.Events);
    }

    [Fact]
    public void Reaching_the_stored_length_submits()
    {
        var machine = CreateInPinEntry();

        Type(machine, "1234");

        Assert.Equal(LockPhase.VerifyingPin, machine.Phase);
        Assert.Equal(
            ["progress:1/4", "progress:2/4", "progress:3/4", "progress:4/4", "phase:VerifyingPin", "verify"],
            _sink.Events);
        Assert.Equal("1234", _sink.Verifications.Single().Pin);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void Every_pin_length_submits_at_its_own_length(int length)
    {
        var machine = CreateInPinEntry(length);

        Type(machine, new string('7', length - 1));
        Assert.Equal(LockPhase.PinEntry, machine.Phase);

        Type(machine, "7");
        Assert.Equal(new string('7', length), _sink.Verifications.Single().Pin);
    }

    [Fact]
    public void Numpad_digits_are_accepted()
    {
        var machine = CreateInPinEntry();

        Press(machine, VirtualKeys.Numpad0 + 9);
        Press(machine, VirtualKeys.Numpad0 + 8);
        Press(machine, VirtualKeys.Numpad0 + 7);
        Press(machine, VirtualKeys.Numpad0 + 6);

        Assert.Equal("9876", _sink.Verifications.Single().Pin);
    }

    [Fact]
    public void Numpad_digits_work_with_num_lock_off()
    {
        var machine = CreateInPinEntry();

        Press(machine, VirtualKeys.End);
        Press(machine, VirtualKeys.DownArrow);
        Press(machine, VirtualKeys.PageDown);
        Press(machine, VirtualKeys.Clear);

        Assert.Equal("1235", _sink.Verifications.Single().Pin);
    }

    [Fact]
    public void Real_navigation_keys_are_not_digits()
    {
        var machine = CreateInPinEntry();

        Press(machine, VirtualKeys.End, isExtended: true);

        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void Enter_submits_a_short_pin()
    {
        var machine = CreateInPinEntry(pinLength: 6);

        Type(machine, "123");
        Press(machine, VirtualKeys.Enter);

        Assert.Equal(LockPhase.VerifyingPin, machine.Phase);
        Assert.Equal("123", _sink.Verifications.Single().Pin);
    }

    [Fact]
    public void Enter_with_nothing_typed_is_ignored_and_costs_no_attempt()
    {
        var machine = CreateInPinEntry();

        Press(machine, VirtualKeys.Enter);

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
        Assert.Empty(_sink.Verifications);
    }

    [Fact]
    public void Backspace_removes_the_last_digit()
    {
        var machine = CreateInPinEntry();

        Type(machine, "19");
        Press(machine, VirtualKeys.Backspace);
        Type(machine, "234");

        Assert.Equal("1234", _sink.Verifications.Single().Pin);
    }

    [Fact]
    public void Backspace_on_an_empty_entry_does_nothing()
    {
        var machine = CreateInPinEntry();

        Press(machine, VirtualKeys.Backspace);

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void Escape_returns_to_locked_and_clears_the_buffer()
    {
        var machine = CreateInPinEntry();

        Type(machine, "12");
        Assert.False(machine.DigitBufferIsClear);

        Press(machine, VirtualKeys.Escape);

        Assert.Equal(LockPhase.Locked, machine.Phase);
        Assert.True(machine.DigitBufferIsClear);
    }

    [Fact]
    public void Other_keys_in_pin_entry_are_consumed_without_effect()
    {
        var machine = CreateInPinEntry();

        Assert.Equal(KeyDownResult.Consumed, Press(machine, VirtualKeys.A));
        Assert.Equal(KeyDownResult.Consumed, Press(machine, UnlockKey));

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void A_held_digit_counts_once()
    {
        var machine = CreateInPinEntry();

        machine.OnKeyDown(Digit0 + 1, false);
        machine.OnKeyDown(Digit0 + 1, false);
        machine.OnKeyDown(Digit0 + 1, false);

        Assert.Equal(["progress:1/4"], _sink.Events);
    }

    [Fact]
    public void Buffer_is_cleared_after_submit()
    {
        var machine = CreateInPinEntry();

        Type(machine, "1234");

        Assert.True(machine.DigitBufferIsClear);
    }

    // PIN timeout

    [Fact]
    public void Pin_entry_times_out_after_20_seconds_without_a_key()
    {
        var machine = CreateInPinEntry();
        Type(machine, "12");

        _clock.Advance(LockStateMachine.PinIdleTimeoutMilliseconds - 1);
        machine.Tick();
        Assert.Equal(LockPhase.PinEntry, machine.Phase);

        _clock.Advance(1);
        machine.Tick();
        Assert.Equal(LockPhase.Locked, machine.Phase);
        Assert.True(machine.DigitBufferIsClear);
    }

    [Fact]
    public void A_key_press_restarts_the_timeout()
    {
        var machine = CreateInPinEntry();

        _clock.Advance(15_000);
        Press(machine, VirtualKeys.A);
        _clock.Advance(15_000);
        machine.Tick();

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
    }

    [Fact]
    public void Auto_repeat_does_not_keep_pin_entry_alive()
    {
        var machine = CreateInPinEntry();
        machine.OnKeyDown(VirtualKeys.A, false);

        for (var i = 0; i < 25; i++)
        {
            _clock.Advance(1_000);
            machine.OnKeyDown(VirtualKeys.A, false);
        }

        Assert.Equal(LockPhase.Locked, machine.Phase);
    }

    [Fact]
    public void Timeout_applies_to_a_key_that_arrives_before_the_next_tick()
    {
        var machine = CreateInPinEntry();
        Type(machine, "12");

        _clock.Advance(LockStateMachine.PinIdleTimeoutMilliseconds);
        var result = Press(machine, Digit0 + 3);

        Assert.Equal(KeyDownResult.Blocked, result);
        Assert.Equal(LockPhase.Locked, machine.Phase);
        Assert.True(machine.DigitBufferIsClear);
    }

    [Fact]
    public void Stuck_verification_times_out_so_the_user_can_retry()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");

        _clock.Advance(LockStateMachine.PinIdleTimeoutMilliseconds);
        machine.Tick();

        Assert.Equal(LockPhase.Locked, machine.Phase);
    }

    // Verification results

    [Fact]
    public void Right_pin_unlocks()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");
        _sink.Events.Clear();

        machine.CompleteVerification(_sink.LastToken, accepted: true);

        Assert.Equal(LockPhase.Unlocked, machine.Phase);
        Assert.Equal(["phase:Unlocked", "unlocked:Pin"], _sink.Events);
    }

    [Fact]
    public void Wrong_pin_returns_to_an_empty_entry()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");
        _sink.Events.Clear();

        machine.CompleteVerification(_sink.LastToken, accepted: false);

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
        Assert.Equal(["rejected", "phase:PinEntry", "progress:0/4"], _sink.Events);
    }

    [Fact]
    public void Wrong_pin_restarts_the_timeout()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");
        _clock.Advance(19_000);
        machine.CompleteVerification(_sink.LastToken, accepted: false);

        _clock.Advance(19_000);
        machine.Tick();

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
    }

    [Fact]
    public void Keys_while_verifying_are_consumed_and_do_not_reach_the_buffer()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");

        Assert.Equal(KeyDownResult.Consumed, Press(machine, Digit0));

        Assert.Equal(LockPhase.VerifyingPin, machine.Phase);
        Assert.True(machine.DigitBufferIsClear);
    }

    [Fact]
    public void Escape_while_verifying_cancels_and_the_result_is_stale()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");
        var token = _sink.LastToken;

        Press(machine, VirtualKeys.Escape);
        Assert.Equal(LockPhase.Locked, machine.Phase);

        machine.CompleteVerification(token, accepted: true);

        Assert.Equal(LockPhase.Locked, machine.Phase);
    }

    [Fact]
    public void Result_after_a_timeout_is_stale()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");
        var token = _sink.LastToken;

        _clock.Advance(LockStateMachine.PinIdleTimeoutMilliseconds);
        machine.Tick();
        machine.CompleteVerification(token, accepted: true);

        Assert.Equal(LockPhase.Locked, machine.Phase);
    }

    [Fact]
    public void Result_of_an_old_attempt_cannot_decide_a_new_one()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");
        var staleToken = _sink.LastToken;

        Press(machine, VirtualKeys.Escape);
        Press(machine, UnlockKey);
        Type(machine, "5678");

        machine.CompleteVerification(staleToken, accepted: true);
        Assert.Equal(LockPhase.VerifyingPin, machine.Phase);

        machine.CompleteVerification(_sink.LastToken, accepted: true);
        Assert.Equal(LockPhase.Unlocked, machine.Phase);
    }

    [Fact]
    public void Stale_wrong_result_costs_no_attempt()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");
        var staleToken = _sink.LastToken;
        Press(machine, VirtualKeys.Escape);
        _sink.Events.Clear();

        machine.CompleteVerification(staleToken, accepted: false);

        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void A_result_is_only_used_once()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");
        var token = _sink.LastToken;

        machine.CompleteVerification(token, accepted: false);
        machine.CompleteVerification(token, accepted: true);

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
    }

    [Fact]
    public void Result_after_unlocking_is_ignored()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");
        var token = _sink.LastToken;
        machine.ForceUnlock(UnlockReason.Exit);
        _sink.Events.Clear();

        machine.CompleteVerification(token, accepted: true);

        Assert.Empty(_sink.Events);
    }

    // Cooldown

    [Fact]
    public void Four_wrong_tries_do_not_start_a_cooldown()
    {
        var machine = Create(pinLength: 4);

        FailAttempts(machine, 4);

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
    }

    [Fact]
    public void Five_wrong_tries_start_a_30_second_cooldown()
    {
        var machine = Create(pinLength: 4);

        FailAttempts(machine, 5);

        Assert.Equal(LockPhase.CoolingDown, machine.Phase);
        Assert.Equal(30_000, machine.CooldownRemainingMilliseconds);
        Assert.Equal("phase:CoolingDown:30000", _sink.Events[^1]);
        Assert.Equal("rejected", _sink.Events[^2]);
    }

    [Fact]
    public void Unlock_key_is_ignored_during_a_cooldown()
    {
        var machine = Create(pinLength: 4);
        FailAttempts(machine, 5);

        Assert.Equal(KeyDownResult.Blocked, Press(machine, UnlockKey));

        Assert.Equal(LockPhase.CoolingDown, machine.Phase);
    }

    [Fact]
    public void Cooldown_counts_down_and_returns_to_locked()
    {
        var machine = Create(pinLength: 4);
        FailAttempts(machine, 5);

        _clock.Advance(29_999);
        machine.Tick();
        Assert.Equal(LockPhase.CoolingDown, machine.Phase);
        Assert.Equal(1, machine.CooldownRemainingMilliseconds);

        _clock.Advance(1);
        machine.Tick();
        Assert.Equal(LockPhase.Locked, machine.Phase);
        Assert.Equal(0, machine.CooldownRemainingMilliseconds);
    }

    [Fact]
    public void Unlock_key_works_the_moment_a_cooldown_ends()
    {
        var machine = Create(pinLength: 4);
        FailAttempts(machine, 5);

        _clock.Advance(30_000);
        Press(machine, UnlockKey);

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
    }

    [Fact]
    public void Cooldowns_double_up_to_five_minutes()
    {
        var machine = Create(pinLength: 4);
        long[] expected = [30_000, 60_000, 120_000, 240_000, 300_000, 300_000, 300_000];

        foreach (var duration in expected)
        {
            FailAttempts(machine, 5);
            Assert.Equal(LockPhase.CoolingDown, machine.Phase);
            Assert.Equal(duration, machine.CooldownRemainingMilliseconds);

            _clock.Advance(duration);
            machine.Tick();
            Assert.Equal(LockPhase.Locked, machine.Phase);
        }
    }

    [Fact]
    public void A_finished_cooldown_gives_five_fresh_tries()
    {
        var machine = Create(pinLength: 4);
        FailAttempts(machine, 5);
        _clock.Advance(30_000);
        machine.Tick();

        FailAttempts(machine, 4);

        Assert.Equal(LockPhase.PinEntry, machine.Phase);
    }

    [Fact]
    public void Escape_does_not_reset_the_wrong_try_count()
    {
        var machine = Create(pinLength: 4);
        FailAttempts(machine, 4);

        Press(machine, VirtualKeys.Escape);
        FailAttempts(machine, 1);

        Assert.Equal(LockPhase.CoolingDown, machine.Phase);
    }

    [Fact]
    public void A_timeout_does_not_reset_the_wrong_try_count()
    {
        var machine = Create(pinLength: 4);
        FailAttempts(machine, 4);

        _clock.Advance(LockStateMachine.PinIdleTimeoutMilliseconds);
        machine.Tick();
        FailAttempts(machine, 1);

        Assert.Equal(LockPhase.CoolingDown, machine.Phase);
    }

    [Fact]
    public void Cooldown_remaining_is_zero_outside_a_cooldown()
    {
        var machine = Create(pinLength: 4);

        Assert.Equal(0, machine.CooldownRemainingMilliseconds);
    }

    // Forced release

    [Fact]
    public void Force_unlock_works_from_locked()
    {
        var machine = Create();

        machine.ForceUnlock(UnlockReason.SessionUnlocked);

        Assert.Equal(LockPhase.Unlocked, machine.Phase);
        Assert.Equal(["phase:Unlocked", "unlocked:SessionUnlocked"], _sink.Events);
    }

    [Fact]
    public void Force_unlock_works_from_pin_entry_and_clears_the_buffer()
    {
        var machine = CreateInPinEntry();
        Type(machine, "12");

        machine.ForceUnlock(UnlockReason.Shutdown);

        Assert.Equal(LockPhase.Unlocked, machine.Phase);
        Assert.True(machine.DigitBufferIsClear);
    }

    [Fact]
    public void Force_unlock_works_from_verifying()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");

        machine.ForceUnlock(UnlockReason.Exit);

        Assert.Equal(LockPhase.Unlocked, machine.Phase);
    }

    [Fact]
    public void Force_unlock_works_from_a_cooldown()
    {
        var machine = Create(pinLength: 4);
        FailAttempts(machine, 5);

        machine.ForceUnlock(UnlockReason.SessionUnlocked);

        Assert.Equal(LockPhase.Unlocked, machine.Phase);
    }

    [Fact]
    public void Force_unlock_when_unlocked_does_nothing()
    {
        var machine = Create();
        machine.ForceUnlock(UnlockReason.Exit);
        _sink.Events.Clear();

        machine.ForceUnlock(UnlockReason.Shutdown);

        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void Tick_when_unlocked_does_nothing()
    {
        var machine = Create();
        machine.ForceUnlock(UnlockReason.Exit);
        _sink.Events.Clear();

        _clock.Advance(1_000_000);
        machine.Tick();

        Assert.Empty(_sink.Events);
    }

    // The drain after an unlock

    private LockStateMachine CreateVerifyingWithLastDigitHeld()
    {
        var machine = CreateInPinEntry();
        Type(machine, "123");
        machine.OnKeyDown(Digit0 + 4, false);
        return machine;
    }

    [Fact]
    public void Unlocking_with_no_key_held_stops_the_hooks_at_once()
    {
        var machine = CreateInPinEntry();
        Type(machine, "1234");

        machine.CompleteVerification(_sink.LastToken, accepted: true);

        Assert.Equal(1, _sink.DrainedCalls);
    }

    [Fact]
    public void The_unlock_key_holds_the_hooks_on_until_it_comes_up()
    {
        var machine = Create();

        machine.OnKeyDown(UnlockKey, false);
        Assert.Equal(LockPhase.Unlocked, machine.Phase);
        Assert.Equal(0, _sink.DrainedCalls);

        machine.OnKeyUp(UnlockKey);
        Assert.Equal(1, _sink.DrainedCalls);
    }

    [Fact]
    public void A_held_last_digit_holds_the_hooks_on_until_it_comes_up()
    {
        var machine = CreateVerifyingWithLastDigitHeld();

        machine.CompleteVerification(_sink.LastToken, accepted: true);
        Assert.Equal(0, _sink.DrainedCalls);

        machine.OnKeyUp(Digit0 + 4);
        Assert.Equal(1, _sink.DrainedCalls);
    }

    [Fact]
    public void Every_held_key_has_to_come_up()
    {
        var machine = CreateVerifyingWithLastDigitHeld();
        machine.OnKeyDown(VirtualKeys.Enter, false);
        machine.CompleteVerification(_sink.LastToken, accepted: true);

        machine.OnKeyUp(Digit0 + 4);
        Assert.Equal(0, _sink.DrainedCalls);

        machine.OnKeyUp(VirtualKeys.Enter);
        Assert.Equal(1, _sink.DrainedCalls);
    }

    [Fact]
    public void The_drain_is_bounded_even_if_a_key_up_never_arrives()
    {
        var machine = Create();
        machine.OnKeyDown(UnlockKey, false);

        _clock.Advance(LockStateMachine.DrainMilliseconds - 1);
        machine.Tick();
        Assert.Equal(0, _sink.DrainedCalls);

        _clock.Advance(1);
        machine.Tick();
        Assert.Equal(1, _sink.DrainedCalls);
    }

    [Fact]
    public void The_drain_ends_only_once()
    {
        var machine = Create();
        machine.OnKeyDown(UnlockKey, false);
        _clock.Advance(LockStateMachine.DrainMilliseconds);
        machine.Tick();

        machine.Tick();
        machine.OnKeyUp(UnlockKey);
        machine.ForceUnlock(UnlockReason.Exit);

        Assert.Equal(1, _sink.DrainedCalls);
    }

    [Fact]
    public void Repeats_of_a_held_key_stay_swallowed_during_the_drain()
    {
        var machine = CreateVerifyingWithLastDigitHeld();
        machine.CompleteVerification(_sink.LastToken, accepted: true);

        Assert.Equal(KeyDownResult.Repeat, machine.OnKeyDown(Digit0 + 4, false));
    }

    [Fact]
    public void Enter_stays_swallowed_during_the_drain_and_keeps_it_open()
    {
        var machine = CreateVerifyingWithLastDigitHeld();
        machine.CompleteVerification(_sink.LastToken, accepted: true);

        Assert.Equal(KeyDownResult.Repeat, machine.OnKeyDown(VirtualKeys.Enter, false));

        machine.OnKeyUp(Digit0 + 4);
        Assert.Equal(0, _sink.DrainedCalls);
    }

    [Fact]
    public void Other_keys_pass_during_the_drain()
    {
        var machine = Create();
        machine.OnKeyDown(UnlockKey, false);

        Assert.Equal(KeyDownResult.NotLocked, machine.OnKeyDown(VirtualKeys.A, false));
    }

    [Fact]
    public void Keys_pass_once_the_drain_is_over()
    {
        var machine = Create();
        Press(machine, UnlockKey);

        Assert.Equal(KeyDownResult.NotLocked, machine.OnKeyDown(VirtualKeys.Enter, false));
    }

    [Theory]
    [InlineData(UnlockReason.SessionUnlocked)]
    [InlineData(UnlockReason.Shutdown)]
    [InlineData(UnlockReason.Exit)]
    [InlineData(UnlockReason.EngineFault)]
    public void Only_the_key_and_pin_drain(UnlockReason reason)
    {
        var machine = Create();
        machine.OnKeyDown(VirtualKeys.A, false);

        machine.ForceUnlock(reason);

        Assert.Equal(1, _sink.DrainedCalls);
    }

    [Fact]
    public void A_forced_release_cuts_the_drain_short()
    {
        var machine = Create();
        machine.OnKeyDown(UnlockKey, false);

        machine.ForceUnlock(UnlockReason.Exit);

        Assert.Equal(1, _sink.DrainedCalls);
    }
}
