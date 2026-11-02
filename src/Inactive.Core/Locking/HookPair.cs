namespace Inactive.Core.Locking;

/// <summary>Seam over the installation of the low level hooks.</summary>
internal interface IHookApi
{
    /// <summary>Installs the keyboard hook. Returns 0 on failure.</summary>
    nint InstallKeyboard();

    /// <summary>Installs the mouse hook. Returns 0 on failure.</summary>
    nint InstallMouse();

    void Remove(nint hook);

    /// <summary>The Win32 error of the last failed install.</summary>
    int LastError { get; }
}

/// <summary>
/// Decides when failing reinstalls have gone on too long. Windows may have dropped the old hooks and
/// nothing can tell, which would leave a lock that neither blocks nor ends, so a lasting failure ends
/// the lock. This goes by elapsed time because a burst of display changes can fail several times in
/// a moment. Used on the hook thread only.
/// </summary>
internal sealed class ReinstallFailureWatch
{
    public const long GiveUpAfterMilliseconds = 30_000;

    private long? _failingSince;

    public void OnSucceeded() => _failingSince = null;

    /// <summary>Records a failure. Returns true when the lock should end.</summary>
    public bool OnFailed(long now)
    {
        _failingSince ??= now;
        return now - _failingSince >= GiveUpAfterMilliseconds;
    }
}

/// <summary>Holds the keyboard and mouse hooks, which only ever exist together.</summary>
internal sealed class HookPair(IHookApi api)
{
    private nint _keyboard;
    private nint _mouse;

    /// <summary>Installs both hooks, or neither. Returns the Win32 error, or 0 on success.</summary>
    public int TryInstall()
    {
        var error = InstallBoth(out _keyboard, out _mouse);
        return error;
    }

    /// <summary>
    /// Replaces both hooks, installing the new ones before removing the old ones so there is no
    /// gap. On failure the old hooks stay. Returns the Win32 error, or 0 on success.
    /// </summary>
    public int Reinstall()
    {
        var error = InstallBoth(out var keyboard, out var mouse);
        if (error != 0)
        {
            return error;
        }

        Remove();
        _keyboard = keyboard;
        _mouse = mouse;
        return 0;
    }

    public void Remove()
    {
        if (_keyboard != 0)
        {
            api.Remove(_keyboard);
            _keyboard = 0;
        }

        if (_mouse != 0)
        {
            api.Remove(_mouse);
            _mouse = 0;
        }
    }

    private int InstallBoth(out nint keyboard, out nint mouse)
    {
        mouse = 0;
        keyboard = api.InstallKeyboard();
        if (keyboard == 0)
        {
            return ErrorOrUnknown();
        }

        mouse = api.InstallMouse();
        if (mouse == 0)
        {
            var error = ErrorOrUnknown();
            api.Remove(keyboard);
            keyboard = 0;
            return error;
        }

        return 0;
    }

    // A failed install must never read as success, even if Windows left no error code.
    private int ErrorOrUnknown() => api.LastError != 0 ? api.LastError : -1;
}
