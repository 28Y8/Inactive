namespace Inactive.Core.Native;

/// <summary>Reads the active keyboard layout. Touches no input, so it is safe anywhere.</summary>
internal static class KeyboardLayout
{
    /// <summary>
    /// Returns the unshifted character on a key in the calling thread's layout, or a null character
    /// for a key with none. The top bit of the Win32 result marks a dead key, which is dropped here
    /// because a dead key is labelled like any other.
    /// </summary>
    public static char GetCharacter(int virtualKey) =>
        (char)(User32.MapVirtualKeyW((uint)virtualKey, NativeConstants.MAPVK_VK_TO_CHAR) & 0xFFFF);
}
