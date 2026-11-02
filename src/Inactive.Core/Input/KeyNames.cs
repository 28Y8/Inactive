using System.Collections.Frozen;
using System.Globalization;
using Inactive.Core.Native;

namespace Inactive.Core.Input;

/// <summary>
/// Maps virtual key codes to names. <see cref="GetName"/> is the fixed form stored in settings, and
/// <see cref="GetDisplayName(int)"/> is what a person should read on their own keyboard.
/// </summary>
public static class KeyNames
{
    private const string FallbackPrefix = "Key 0x";

    private static readonly FrozenDictionary<int, string> NamedKeys = new Dictionary<int, string>
    {
        [VirtualKeys.Backspace] = "Backspace",
        [VirtualKeys.Tab] = "Tab",
        [VirtualKeys.Clear] = "Clear",
        [VirtualKeys.Enter] = "Enter",
        [VirtualKeys.Shift] = "Shift",
        [VirtualKeys.Control] = "Ctrl",
        [VirtualKeys.Alt] = "Alt",
        [VirtualKeys.Pause] = "Pause",
        [VirtualKeys.CapsLock] = "Caps Lock",
        [VirtualKeys.Escape] = "Escape",
        [VirtualKeys.Space] = "Space",
        [VirtualKeys.PageUp] = "Page Up",
        [VirtualKeys.PageDown] = "Page Down",
        [VirtualKeys.End] = "End",
        [VirtualKeys.Home] = "Home",
        [VirtualKeys.LeftArrow] = "Left Arrow",
        [VirtualKeys.UpArrow] = "Up Arrow",
        [VirtualKeys.RightArrow] = "Right Arrow",
        [VirtualKeys.DownArrow] = "Down Arrow",
        [VirtualKeys.PrintScreen] = "Print Screen",
        [VirtualKeys.Insert] = "Insert",
        [VirtualKeys.Delete] = "Delete",
        [VirtualKeys.LeftWindows] = "Left Windows",
        [VirtualKeys.RightWindows] = "Right Windows",
        [VirtualKeys.Menu] = "Menu",
        [VirtualKeys.NumpadMultiply] = "Numpad Multiply",
        [VirtualKeys.NumpadAdd] = "Numpad Add",
        [VirtualKeys.NumpadSubtract] = "Numpad Subtract",
        [VirtualKeys.NumpadDecimal] = "Numpad Decimal",
        [VirtualKeys.NumpadDivide] = "Numpad Divide",
        [VirtualKeys.NumLock] = "Num Lock",
        [VirtualKeys.ScrollLock] = "Scroll Lock",
        [VirtualKeys.LeftShift] = "Left Shift",
        [VirtualKeys.RightShift] = "Right Shift",
        [VirtualKeys.LeftControl] = "Left Ctrl",
        [VirtualKeys.RightControl] = "Right Ctrl",
        [VirtualKeys.LeftAlt] = "Left Alt",
        [VirtualKeys.RightAlt] = "Right Alt",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'",
    }.ToFrozenDictionary();

    // Display only: the stored form of these keys stays the hex name, which existing settings use.
    private static readonly FrozenDictionary<int, string> MediaKeys = new Dictionary<int, string>
    {
        [0xA6] = "Browser Back",
        [0xA7] = "Browser Forward",
        [0xA8] = "Browser Refresh",
        [0xA9] = "Browser Stop",
        [0xAA] = "Browser Search",
        [0xAB] = "Browser Favorites",
        [0xAC] = "Browser Home",
        [0xAD] = "Volume Mute",
        [0xAE] = "Volume Down",
        [0xAF] = "Volume Up",
        [0xB0] = "Next Track",
        [0xB1] = "Previous Track",
        [0xB2] = "Stop Media",
        [0xB3] = "Play or Pause",
        [0xB4] = "Mail",
        [0xB5] = "Media Select",
        [0xB6] = "App 1",
        [0xB7] = "App 2",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, int> KeysByName = BuildReverseLookup();

    /// <summary>
    /// Returns the fixed name of a virtual key, such as "Right Shift" or "F5". Settings store this
    /// form, so it must not change. Punctuation names follow a US keyboard.
    /// </summary>
    public static string GetName(int virtualKey)
    {
        if (NamedKeys.TryGetValue(virtualKey, out var name))
        {
            return name;
        }

        return virtualKey switch
        {
            >= VirtualKeys.D0 and <= VirtualKeys.D9 => ((char)virtualKey).ToString(),
            >= VirtualKeys.A and <= VirtualKeys.Z => ((char)virtualKey).ToString(),
            >= VirtualKeys.Numpad0 and <= VirtualKeys.Numpad9 =>
                "Numpad " + (virtualKey - VirtualKeys.Numpad0).ToString(CultureInfo.InvariantCulture),
            >= VirtualKeys.F1 and <= VirtualKeys.F24 =>
                "F" + (virtualKey - VirtualKeys.F1 + 1).ToString(CultureInfo.InvariantCulture),
            _ => FallbackPrefix + virtualKey.ToString("X2", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Returns the name to show a person. Punctuation keys follow the keyboard layout of the calling
    /// thread, because the code behind a key says little about what is printed on it.
    /// </summary>
    public static string GetDisplayName(int virtualKey) => GetDisplayName(virtualKey, KeyboardLayout.GetCharacter);

    /// <summary>Returns the name to show a person, with the layout lookup supplied.</summary>
    /// <param name="virtualKey">The key.</param>
    /// <param name="layoutCharacter">Returns the character on a key, or a control character for none.</param>
    internal static string GetDisplayName(int virtualKey, Func<int, char> layoutCharacter)
    {
        if (MediaKeys.TryGetValue(virtualKey, out var mediaName))
        {
            return mediaName;
        }

        var name = GetName(virtualKey);
        if (!IsPunctuationKey(virtualKey) && !name.StartsWith(FallbackPrefix, StringComparison.Ordinal))
        {
            return name;
        }

        var character = layoutCharacter(virtualKey);
        return character > ' ' ? character.ToString() : name;
    }

    /// <summary>True for the keys that type punctuation or accented letters, which vary by layout.</summary>
    internal static bool IsPunctuationKey(int virtualKey) => virtualKey is
        (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2;

    /// <summary>Finds a key by its display name or a common alias. Ignores case and surrounding spaces.</summary>
    public static bool TryGetKey(string? name, out int virtualKey)
    {
        virtualKey = 0;
        return !string.IsNullOrWhiteSpace(name) && KeysByName.TryGetValue(name.Trim(), out virtualKey);
    }

    /// <summary>
    /// Maps a key press to a PIN digit. Accepts the top row and the numpad. With Num Lock off the
    /// numpad reports navigation keys that are told apart from the real ones by the extended flag.
    /// Allocation free, because it runs inside the keyboard hook.
    /// </summary>
    public static bool TryGetPinDigit(int virtualKey, bool isExtended, out char digit)
    {
        var value = virtualKey switch
        {
            >= VirtualKeys.D0 and <= VirtualKeys.D9 => virtualKey - VirtualKeys.D0,
            >= VirtualKeys.Numpad0 and <= VirtualKeys.Numpad9 => virtualKey - VirtualKeys.Numpad0,
            VirtualKeys.Insert when !isExtended => 0,
            VirtualKeys.End when !isExtended => 1,
            VirtualKeys.DownArrow when !isExtended => 2,
            VirtualKeys.PageDown when !isExtended => 3,
            VirtualKeys.LeftArrow when !isExtended => 4,
            VirtualKeys.Clear when !isExtended => 5,
            VirtualKeys.RightArrow when !isExtended => 6,
            VirtualKeys.Home when !isExtended => 7,
            VirtualKeys.UpArrow when !isExtended => 8,
            VirtualKeys.PageUp when !isExtended => 9,
            _ => -1,
        };

        digit = value < 0 ? '\0' : (char)('0' + value);
        return value >= 0;
    }

    private static FrozenDictionary<string, int> BuildReverseLookup()
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var key = 1; key < 0xFF; key++)
        {
            lookup[GetName(key)] = key;
        }

        lookup["Control"] = VirtualKeys.Control;
        lookup["Esc"] = VirtualKeys.Escape;
        lookup["Return"] = VirtualKeys.Enter;
        lookup["Del"] = VirtualKeys.Delete;
        lookup["Ins"] = VirtualKeys.Insert;
        lookup["PgUp"] = VirtualKeys.PageUp;
        lookup["PgDn"] = VirtualKeys.PageDown;
        lookup["Win"] = VirtualKeys.LeftWindows;
        lookup["Windows"] = VirtualKeys.LeftWindows;

        return lookup.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
