using System.Windows.Interop;
using Inactive.Core.Input;
using Inactive.Core.Logging;
using Inactive.Native;

namespace Inactive.Services;

/// <summary>The system wide lock hotkey. Used from the UI thread.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 1;

    private readonly HwndSource _window;
    private HotkeyGesture? _gesture;
    private bool _isRegistered;
    private bool _isSuspended;

    public GlobalHotkey()
    {
        _window = new HwndSource(new HwndSourceParameters("Inactive hotkey", 0, 0) { WindowStyle = 0 });
        _window.AddHook(OnWindowMessage);
    }

    public event EventHandler? Pressed;

    /// <summary>The hotkey in force, or null when it is off or Windows refused it.</summary>
    public HotkeyGesture? ActiveGesture => _isRegistered ? _gesture : null;

    /// <summary>True when a hotkey is configured but Windows refused it, usually because another app owns it.</summary>
    public bool HasFailed => _gesture is not null && !_isRegistered && !_isSuspended;

    /// <summary>Replaces the hotkey. Null turns it off.</summary>
    public void Apply(HotkeyGesture? gesture)
    {
        if (gesture == _gesture)
        {
            return;
        }

        Unregister();
        _gesture = gesture;
        if (!_isSuspended)
        {
            Register();
        }
    }

    /// <summary>Stops listening while a new hotkey is being recorded, so the old one can be pressed.</summary>
    public void Suspend()
    {
        _isSuspended = true;
        Unregister();
    }

    public void Resume()
    {
        _isSuspended = false;
        Register();
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHook(OnWindowMessage);
        _window.Dispose();
    }

    private void Register()
    {
        if (_gesture is not { IsValid: true } gesture)
        {
            return;
        }

        _isRegistered = NativeMethods.RegisterHotKey(
            _window.Handle,
            HotkeyId,
            (uint)gesture.Modifiers | NativeMethods.ModNoRepeat,
            (uint)gesture.Key);

        if (!_isRegistered)
        {
            AppLog.Warn("The lock hotkey could not be registered.");
        }
    }

    private void Unregister()
    {
        if (_isRegistered)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId);
            _isRegistered = false;
        }
    }

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }
}
