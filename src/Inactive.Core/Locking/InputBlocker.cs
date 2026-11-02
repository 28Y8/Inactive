using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Inactive.Core.Logging;
using Inactive.Core.Native;

namespace Inactive.Core.Locking;

/// <summary>What the hook thread reports. Everything runs on the hook thread.</summary>
internal interface IBlockerHost
{
    /// <summary>Both hooks are on. Called before the first input event can arrive.</summary>
    void OnInstalled();

    /// <summary>Called a few times per second while locked.</summary>
    void OnTick();

    void OnReinstallFailed(int win32Error);

    /// <summary>The hooks are off. Called once, and only after <see cref="OnInstalled"/>.</summary>
    void OnStopped();
}

/// <summary>Outcome of installing the hooks.</summary>
internal readonly record struct HookStartResult(bool Installed, int Win32Error);

/// <summary>Seam over the hook thread, so the controller can be tested without touching input.</summary>
internal interface IInputBlocker
{
    /// <summary>Blocks until the hooks are on or have failed to go on.</summary>
    HookStartResult Start(InputFilter filter, IBlockerHost host);

    /// <summary>Asks the hook thread to remove the hooks and exit. Non blocking, allocation free.</summary>
    void RequestStop();

    /// <summary>Asks the hook thread to replace the hooks now rather than at the next interval.</summary>
    void RequestReinstall();
}

/// <summary>
/// Owns the low level hooks on a dedicated thread with its own message loop. All decisions run on
/// this thread, so the unlock key works however busy or hung the UI thread is.
/// </summary>
internal sealed unsafe class InputBlocker : IInputBlocker
{
    private const uint TickMilliseconds = 250;
    private const long ReinstallIntervalMilliseconds = 10_000;

    private const uint WakeMessage = NativeConstants.WM_APP + 1;

    // The callbacks are static so that no delegate can be collected. Only one lock session runs at
    // a time, so one slot is enough.
    private static InputFilter? s_filter;

    private readonly ManualResetEventSlim _started = new();
    private InputFilter? _filter;
    private IBlockerHost? _host;
    private HookStartResult _startResult;
    private volatile uint _threadId;
    private volatile bool _stopRequested;
    private volatile bool _reinstallRequested;

    public HookStartResult Start(InputFilter filter, IBlockerHost host)
    {
        _filter = filter;
        _host = host;

        // A foreground thread: if the UI ends without releasing, the lock and its unlock key stay
        // alive until someone presses the key.
        var thread = new Thread(Run)
        {
            IsBackground = false,
            Priority = ThreadPriority.Highest,
            Name = "Inactive input hooks",
        };
        thread.Start();

        _started.Wait();
        return _startResult;
    }

    public void RequestStop()
    {
        _stopRequested = true;
        Wake();
    }

    public void RequestReinstall()
    {
        _reinstallRequested = true;
        Wake();
    }

    private void Wake()
    {
        var threadId = _threadId;
        if (threadId != 0)
        {
            User32.PostThreadMessageW(threadId, WakeMessage, 0, 0);
        }
    }

    private void Run()
    {
        var hooks = new HookPair(new Win32HookApi());
        var host = _host!;
        var installed = false;
        nuint timer = 0;

        try
        {
            // PostThreadMessage needs a message queue, and the first Peek creates it.
            MSG message;
            User32.PeekMessageW(&message, 0, NativeConstants.WM_APP, NativeConstants.WM_APP, NativeConstants.PM_NOREMOVE);
            _threadId = Kernel32.GetCurrentThreadId();

            s_filter = _filter;
            var error = hooks.TryInstall();
            if (error != 0)
            {
                _startResult = new HookStartResult(false, error);
                return;
            }

            timer = User32.SetTimer(0, 0, TickMilliseconds, 0);
            if (timer == 0)
            {
                _startResult = new HookStartResult(false, Marshal.GetLastPInvokeError());
                return;
            }

            // Input callbacks only run while this thread pumps, so the host sees this first.
            installed = true;
            host.OnInstalled();
            _startResult = new HookStartResult(true, 0);
            _started.Set();

            Pump(hooks, host);
        }
        catch (Exception e)
        {
            AppLog.Error("The hook thread failed. Releasing the lock.", e);
            if (!installed)
            {
                _startResult = new HookStartResult(false, -1);
            }
        }
        finally
        {
            if (timer != 0)
            {
                User32.KillTimer(0, timer);
            }

            hooks.Remove();
            s_filter = null;
            _started.Set();

            if (installed)
            {
                host.OnStopped();
            }
        }
    }

    private void Pump(HookPair hooks, IBlockerHost host)
    {
        var lastReinstall = Environment.TickCount64;
        var reinstallFailures = new ReinstallFailureWatch();
        MSG message;

        // GetMessage returns 0 on WM_QUIT and -1 on error. Either way the lock lets go.
        while (!_stopRequested && User32.GetMessageW(&message, 0, 0, 0) > 0)
        {
            if (message.message is not (NativeConstants.WM_TIMER or WakeMessage) || _stopRequested)
            {
                continue;
            }

            host.OnTick();

            var now = Environment.TickCount64;
            if (_reinstallRequested || now - lastReinstall >= ReinstallIntervalMilliseconds)
            {
                _reinstallRequested = false;
                lastReinstall = now;

                // Windows silently drops a hook that once ran too long, so replace them on a
                // schedule. No callback can run between the two steps: this thread is not pumping.
                var error = hooks.Reinstall();
                if (error == 0)
                {
                    reinstallFailures.OnSucceeded();
                    continue;
                }

                host.OnReinstallFailed(error);

                // Returning runs the teardown in Run, which ends the lock with EngineFault.
                if (reinstallFailures.OnFailed(now))
                {
                    AppLog.Error("Input hooks could not be refreshed for too long. Releasing the lock.");
                    return;
                }
            }
        }
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        var filter = s_filter;
        if (code == NativeConstants.HC_ACTION && filter is not null)
        {
            var data = (KBDLLHOOKSTRUCT*)lParam;
            var isKeyDown = (uint)wParam is NativeConstants.WM_KEYDOWN or NativeConstants.WM_SYSKEYDOWN;
            var isExtended = (data->flags & NativeConstants.LLKHF_EXTENDED) != 0;

            if (filter.OnKey(isKeyDown, (int)data->vkCode, isExtended))
            {
                return 1;
            }
        }

        return User32.CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static nint MouseProc(int code, nint wParam, nint lParam)
    {
        var filter = s_filter;
        if (code == NativeConstants.HC_ACTION && filter is not null)
        {
            var data = (MSLLHOOKSTRUCT*)lParam;
            var isInjected = (data->flags & NativeConstants.LLMHF_INJECTED) != 0;

            if (filter.OnMouse((uint)wParam, data->pt.X, data->pt.Y, isInjected))
            {
                return 1;
            }
        }

        return User32.CallNextHookEx(0, code, wParam, lParam);
    }

    private sealed class Win32HookApi : IHookApi
    {
        private readonly nint _module = Kernel32.GetModuleHandleW(null);

        public int LastError { get; private set; }

        public nint InstallKeyboard() => Install(NativeConstants.WH_KEYBOARD_LL, &KeyboardProc);

        public nint InstallMouse() => Install(NativeConstants.WH_MOUSE_LL, &MouseProc);

        public void Remove(nint hook) => User32.UnhookWindowsHookEx(hook);

        private nint Install(int kind, delegate* unmanaged<int, nint, nint, nint> callback)
        {
            var hook = User32.SetWindowsHookExW(kind, callback, _module, 0);
            if (hook == 0)
            {
                LastError = Marshal.GetLastPInvokeError();
            }

            return hook;
        }
    }
}
