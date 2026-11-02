using Inactive.Core.Security;
using Inactive.Core.Settings;

namespace Inactive.Core.Locking;

/// <summary>What a lock needs to know.</summary>
/// <param name="UnlockKey">The virtual key that ends the lock.</param>
/// <param name="Pin">The stored PIN, or null for none.</param>
/// <param name="AllowDisplayOff">Keep the PC awake but let the display turn off.</param>
public sealed record LockRequest(int UnlockKey, PinCredential? Pin = null, bool AllowDisplayOff = false)
{
    public static LockRequest FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new LockRequest(settings.UnlockKey, settings.Pin, settings.AllowDisplayOff);
    }
}

public enum LockError
{
    None,
    AlreadyLocked,

    /// <summary>The unlock key could never be pressed or recognised, so locking would be a trap.</summary>
    InvalidUnlockKey,

    /// <summary>The stored PIN is damaged, so it could never be entered correctly.</summary>
    InvalidPin,

    /// <summary>Windows refused a hook. Nothing stayed on.</summary>
    HookInstallFailed,
}

/// <summary>Outcome of <see cref="LockController.TryLock"/>.</summary>
/// <param name="Error">What went wrong, or <see cref="LockError.None"/>.</param>
/// <param name="Win32Error">The Win32 error behind a hook failure, otherwise 0.</param>
public readonly record struct LockResult(LockError Error, int Win32Error = 0)
{
    public bool Succeeded => Error == LockError.None;

    /// <summary>A sentence to show the user, or an empty string on success.</summary>
    public string Message => Error switch
    {
        LockError.None => string.Empty,
        LockError.AlreadyLocked => "This PC is already locked.",
        LockError.InvalidUnlockKey => "The unlock key can't be used. Choose another one in Settings, then try again.",
        LockError.InvalidPin => "The saved PIN can't be read. Set a new PIN in Settings, then try again.",
        LockError.HookInstallFailed => "Inactive couldn't take over the keyboard and mouse, so your PC was not locked.",
        _ => "Your PC was not locked.",
    };
}

public sealed class LockPhaseChangedEventArgs(LockPhase phase, TimeSpan cooldownRemaining) : EventArgs
{
    public LockPhase Phase { get; } = phase;

    /// <summary>How long the cooldown lasts when entering <see cref="LockPhase.CoolingDown"/>, otherwise zero.</summary>
    public TimeSpan CooldownRemaining { get; } = cooldownRemaining;
}

public sealed class UnlockedEventArgs(UnlockReason reason) : EventArgs
{
    public UnlockReason Reason { get; } = reason;
}

public sealed class PinProgressEventArgs(int entered, int length) : EventArgs
{
    public int Entered { get; } = entered;

    public int Length { get; } = length;
}

#if DEBUG
/// <summary>Counters from the last finished lock, for the debug smoke test.</summary>
public readonly record struct LockSessionStats(long KeyboardEventsSwallowed, long MouseEventsSwallowed);
#endif
