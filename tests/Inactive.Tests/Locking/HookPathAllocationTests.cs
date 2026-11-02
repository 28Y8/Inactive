using Inactive.Core.Input;
using Inactive.Core.Locking;
using Inactive.Core.Native;
using Inactive.Core.Security;
using Inactive.Tests.TestSupport;

namespace Inactive.Tests.Locking;

/// <summary>
/// A hook callback that triggers a garbage collection can run past the hook timeout, and Windows
/// then silently removes the hook. So this drives every path a callback can take through the real
/// filter, state machine, event queue and verifier hand off, and measures the managed allocations.
/// </summary>
public sealed class HookPathAllocationTests
{
    private const int UnlockKey = VirtualKeys.RightShift;

    private static void Press(InputFilter filter, int key)
    {
        filter.OnKey(true, key, false);
        filter.OnKey(false, key, false);
    }

    private static void DriveEveryPath(InputFilter filter, FakeLockPlatform platform)
    {
        platform.Advance(InputFilter.AttemptThrottleMilliseconds);
        Press(filter, VirtualKeys.A);

        filter.OnMouse(NativeConstants.WM_MOUSEMOVE, 1, 2, isInjected: false);
        filter.OnMouse(NativeConstants.WM_MOUSEMOVE, 1, 2, isInjected: true);
        filter.OnMouse(NativeConstants.WM_LBUTTONDOWN, 1, 2, isInjected: false);
        filter.OnMouse(NativeConstants.WM_LBUTTONUP, 1, 2, isInjected: false);
        filter.OnMouse(NativeConstants.WM_MOUSEWHEEL, 1, 2, isInjected: false);

        Press(filter, UnlockKey);
        Press(filter, VirtualKeys.D0 + 1);
        Press(filter, VirtualKeys.Backspace);
        Press(filter, VirtualKeys.Numpad0 + 1);
        Press(filter, VirtualKeys.A);
        Press(filter, VirtualKeys.Escape);

        // Four digits submit, which hands off to the verifier. Escape then cancels the check.
        Press(filter, UnlockKey);
        Press(filter, VirtualKeys.D0);
        Press(filter, VirtualKeys.D0 + 1);
        Press(filter, VirtualKeys.D0 + 2);
        Press(filter, VirtualKeys.D0 + 3);
        Press(filter, VirtualKeys.Escape);
    }

    [Fact]
    public void Every_callback_path_is_allocation_free()
    {
        var platform = new FakeLockPlatform();
        using var controller = new LockController(platform);
        controller.TryLock(new LockRequest(UnlockKey, PinHasher.Create("1234", 1)));
        var filter = platform.Blocker.Filter!;

        // The first calls initialise statics and JIT stubs, which is not what is being measured.
        for (var i = 0; i < 100; i++)
        {
            DriveEveryPath(filter, platform);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            DriveEveryPath(filter, platform);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
