namespace Inactive.Core.Input;

public enum UnlockKeyVerdict
{
    Allowed,

    /// <summary>Usable, but easy to press by accident, so the picker should warn.</summary>
    AllowedWithWarning,

    Refused,
}

/// <summary>Decides which keys may serve as the unlock key.</summary>
public static class UnlockKeyRules
{
    public static UnlockKeyVerdict Evaluate(int virtualKey) => virtualKey switch
    {
        < VirtualKeys.LeftMouseButton or > 0xFE => UnlockKeyVerdict.Refused,

        // A low level keyboard hook never reports these, so such a key could never unlock.
        >= VirtualKeys.LeftMouseButton and <= VirtualKeys.ExtraMouseButton2 => UnlockKeyVerdict.Refused,
        VirtualKeys.Shift or VirtualKeys.Control or VirtualKeys.Alt => UnlockKeyVerdict.Refused,
        VirtualKeys.ProcessKey or VirtualKeys.Packet => UnlockKeyVerdict.Refused,

        // These have a job during PIN entry or are too commonly pressed.
        VirtualKeys.Enter or VirtualKeys.Escape or VirtualKeys.Tab or VirtualKeys.Space => UnlockKeyVerdict.Refused,
        VirtualKeys.Backspace => UnlockKeyVerdict.Refused,
        >= VirtualKeys.D0 and <= VirtualKeys.D9 => UnlockKeyVerdict.Refused,
        >= VirtualKeys.Numpad0 and <= VirtualKeys.Numpad9 => UnlockKeyVerdict.Refused,
        VirtualKeys.LeftWindows or VirtualKeys.RightWindows => UnlockKeyVerdict.Refused,

        >= VirtualKeys.A and <= VirtualKeys.Z => UnlockKeyVerdict.AllowedWithWarning,

        _ => UnlockKeyVerdict.Allowed,
    };
}
