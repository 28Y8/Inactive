namespace Inactive.Core.Locking;

/// <summary>Where the lock currently stands.</summary>
public enum LockPhase
{
    Unlocked,
    Locked,

    /// <summary>The unlock key was pressed and a PIN is being typed.</summary>
    PinEntry,

    /// <summary>A PIN was submitted and is being checked off the hook thread.</summary>
    VerifyingPin,

    /// <summary>Too many wrong PINs. The unlock key does nothing until the cooldown ends.</summary>
    CoolingDown,
}

/// <summary>Why the lock ended.</summary>
public enum UnlockReason
{
    UnlockKey,
    Pin,
    SessionUnlocked,
    Shutdown,
    Exit,

    /// <summary>The engine hit an unexpected error and let go, because the lock fails open.</summary>
    EngineFault,

#if DEBUG
    DemoTimer,
#endif
}
