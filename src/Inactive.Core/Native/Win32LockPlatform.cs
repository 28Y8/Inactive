using System.Runtime.InteropServices;
using Inactive.Core.Locking;
using Inactive.Core.Power;

namespace Inactive.Core.Native;

/// <summary>
/// The real implementation of the lock engine seams. This is the only place in the lock engine
/// outside <see cref="InputBlocker"/> that touches Win32, so tests stay away from it by using fakes.
/// </summary>
internal sealed class Win32LockPlatform : ILockPlatform
{
    public ICursorApi Cursor { get; } = new Win32CursorApi();

    public IForegroundProbe Foreground { get; } = new Win32ForegroundProbe();

    public IExecutionStateApi ExecutionState { get; } = new Win32ExecutionStateApi();

    public IInputBlocker CreateInputBlocker() => new InputBlocker();

    public bool IsKeyDown(int virtualKey) => (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public long NowMilliseconds() => Environment.TickCount64;
}

internal sealed class Win32CursorApi : ICursorApi
{
    public ScreenSize GetPrimaryScreenSize() =>
        new(User32.GetSystemMetrics(NativeConstants.SM_CXSCREEN), User32.GetSystemMetrics(NativeConstants.SM_CYSCREEN));

    public void SetPosition(int x, int y) => User32.SetCursorPos(x, y);
}

internal sealed class Win32ExecutionStateApi : IExecutionStateApi
{
    public uint SetExecutionState(uint flags) => Kernel32.SetThreadExecutionState(flags);
}

internal sealed unsafe class Win32ForegroundProbe : IForegroundProbe
{
    public bool IsForegroundElevated()
    {
        var window = User32.GetForegroundWindow();
        if (window == 0)
        {
            return false;
        }

        uint processId = 0;
        User32.GetWindowThreadProcessId(window, &processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        var process = Kernel32.OpenProcess(NativeConstants.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            return Marshal.GetLastPInvokeError() == NativeConstants.ERROR_ACCESS_DENIED;
        }

        try
        {
            return IsElevated(process);
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    private static bool IsElevated(nint process)
    {
        nint token = 0;
        if (!Advapi32.OpenProcessToken(process, NativeConstants.TOKEN_QUERY, &token))
        {
            return Marshal.GetLastPInvokeError() == NativeConstants.ERROR_ACCESS_DENIED;
        }

        try
        {
            var elevation = default(TOKEN_ELEVATION);
            uint length = 0;
            var succeeded = Advapi32.GetTokenInformation(
                token,
                NativeConstants.TokenElevation,
                &elevation,
                (uint)sizeof(TOKEN_ELEVATION),
                &length);

            return succeeded && elevation.TokenIsElevated != 0;
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }
}
