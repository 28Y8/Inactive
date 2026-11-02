using Inactive.Core.Locking;

namespace Inactive.Tests.Locking;

public sealed class ReinstallFailureWatchTests
{
    private const long Start = 5_000;

    private readonly ReinstallFailureWatch _watch = new();

    [Fact]
    public void One_failure_is_no_reason_to_end_the_lock() => Assert.False(_watch.OnFailed(Start));

    [Fact]
    public void A_burst_of_failures_is_no_reason_to_end_the_lock()
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.False(_watch.OnFailed(Start + i));
        }
    }

    [Fact]
    public void Failing_for_the_whole_period_ends_the_lock()
    {
        _watch.OnFailed(Start);

        Assert.False(_watch.OnFailed(Start + ReinstallFailureWatch.GiveUpAfterMilliseconds - 1));
        Assert.True(_watch.OnFailed(Start + ReinstallFailureWatch.GiveUpAfterMilliseconds));
    }

    [Fact]
    public void A_success_starts_the_period_again()
    {
        _watch.OnFailed(Start);
        _watch.OnSucceeded();

        Assert.False(_watch.OnFailed(Start + ReinstallFailureWatch.GiveUpAfterMilliseconds));
    }
}

public sealed class HookPairTests
{
    private const int AccessDenied = 5;

    private readonly ScriptedHookApi _api = new();

    [Fact]
    public void Installs_both_hooks()
    {
        var hooks = new HookPair(_api);

        Assert.Equal(0, hooks.TryInstall());
        Assert.Equal(["install keyboard 1", "install mouse 2"], _api.Log);
    }

    [Fact]
    public void Keyboard_failure_reports_the_error_and_leaves_nothing_on()
    {
        _api.FailKeyboard = true;
        var hooks = new HookPair(_api);

        Assert.Equal(AccessDenied, hooks.TryInstall());
        Assert.Empty(_api.Live);
    }

    [Fact]
    public void Mouse_failure_removes_the_keyboard_hook()
    {
        _api.FailMouse = true;
        var hooks = new HookPair(_api);

        Assert.Equal(AccessDenied, hooks.TryInstall());
        Assert.Empty(_api.Live);
    }

    [Fact]
    public void Failure_without_an_error_code_still_reads_as_failure()
    {
        _api.FailMouse = true;
        _api.FailureCode = 0;
        var hooks = new HookPair(_api);

        Assert.NotEqual(0, hooks.TryInstall());
    }

    [Fact]
    public void Reinstall_puts_new_hooks_on_before_removing_old_ones()
    {
        var hooks = new HookPair(_api);
        hooks.TryInstall();
        _api.Log.Clear();

        Assert.Equal(0, hooks.Reinstall());

        Assert.Equal(["install keyboard 3", "install mouse 4", "remove 1", "remove 2"], _api.Log);
        Assert.Equal([3, 4], _api.Live.Order());
    }

    [Fact]
    public void Failed_reinstall_keeps_the_old_hooks()
    {
        var hooks = new HookPair(_api);
        hooks.TryInstall();
        _api.FailMouse = true;

        Assert.Equal(AccessDenied, hooks.Reinstall());

        Assert.Equal([1, 2], _api.Live.Order());
    }

    [Fact]
    public void Remove_takes_both_hooks_off_once()
    {
        var hooks = new HookPair(_api);
        hooks.TryInstall();

        hooks.Remove();
        hooks.Remove();

        Assert.Empty(_api.Live);
        Assert.Equal(2, _api.Log.Count(line => line.StartsWith("remove", StringComparison.Ordinal)));
    }

    private sealed class ScriptedHookApi : IHookApi
    {
        private nint _next;

        public bool FailKeyboard { get; set; }

        public bool FailMouse { get; set; }

        public int FailureCode { get; set; } = AccessDenied;

        public List<string> Log { get; } = [];

        public HashSet<nint> Live { get; } = [];

        public int LastError => FailureCode;

        public nint InstallKeyboard() => Install("keyboard", FailKeyboard);

        public nint InstallMouse() => Install("mouse", FailMouse);

        public void Remove(nint hook)
        {
            Log.Add($"remove {hook}");
            Live.Remove(hook);
        }

        private nint Install(string kind, bool fail)
        {
            if (fail)
            {
                return 0;
            }

            var hook = ++_next;
            Log.Add($"install {kind} {hook}");
            Live.Add(hook);
            return hook;
        }
    }
}
