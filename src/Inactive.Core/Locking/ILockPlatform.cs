using Inactive.Core.Power;

namespace Inactive.Core.Locking;

/// <summary>Everything the lock engine needs from Windows, gathered in one seam.</summary>
internal interface ILockPlatform
{
    ICursorApi Cursor { get; }

    IForegroundProbe Foreground { get; }

    IExecutionStateApi ExecutionState { get; }

    IInputBlocker CreateInputBlocker();

    bool IsKeyDown(int virtualKey);

    long NowMilliseconds();
}
