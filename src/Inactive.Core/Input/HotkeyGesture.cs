using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inactive.Core.Input;

/// <summary>Hotkey modifiers. The values match the fsModifiers flags of RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

/// <summary>
/// A global hotkey: modifiers plus one key. Named to stay clear of the KeyGesture type in WPF.
/// Stored as text such as "Ctrl+Alt+L".
/// </summary>
[JsonConverter(typeof(HotkeyGestureJsonConverter))]
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int Key)
{
    private static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Control, "Ctrl"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Shift, "Shift"),
        (HotkeyModifiers.Windows, "Win"),
    ];

    public static HotkeyGesture Default { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKeys.L);

    /// <summary>True when the gesture has a modifier and one key that is not itself a modifier.</summary>
    public bool IsValid =>
        Modifiers != HotkeyModifiers.None && Key is > 0 and < 0xFF && !IsModifierKey(Key);

    /// <summary>
    /// True for Shift plus a key that types something, such as Shift + L. As a global hotkey that
    /// would take the capital L away from every app, so the recorder refuses it. A stored gesture
    /// like this still loads, because it was the person's own choice.
    /// </summary>
    public bool TypesCharacter =>
        Modifiers == HotkeyModifiers.Shift
        && (Key is (>= VirtualKeys.D0 and <= VirtualKeys.D9) or (>= VirtualKeys.A and <= VirtualKeys.Z)
            || KeyNames.IsPunctuationKey(Key));

    /// <summary>The form shown in the UI, such as "Ctrl + Alt + L".</summary>
    public string DisplayText => Format(" + ", KeyNames.GetDisplayName(Key));

    /// <summary>The stored form, such as "Ctrl+Alt+L".</summary>
    public override string ToString() => Format("+", KeyNames.GetName(Key));

    public static HotkeyGesture Parse(string text) =>
        TryParse(text, out var gesture) ? gesture : throw new FormatException($"'{text}' is not a valid hotkey.");

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        var key = 0;

        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (TryParseModifier(part, out var modifier))
            {
                if (modifiers.HasFlag(modifier))
                {
                    return false;
                }

                modifiers |= modifier;
            }
            else if (key != 0 || !KeyNames.TryGetKey(part, out key))
            {
                return false;
            }
        }

        var parsed = new HotkeyGesture(modifiers, key);
        if (!parsed.IsValid)
        {
            return false;
        }

        gesture = parsed;
        return true;
    }

    private string Format(string separator, string keyName)
    {
        var builder = new StringBuilder();

        foreach (var (flag, name) in ModifierNames)
        {
            if (Modifiers.HasFlag(flag))
            {
                builder.Append(name).Append(separator);
            }
        }

        return builder.Append(keyName).ToString();
    }

    private static bool TryParseModifier(string text, out HotkeyModifiers modifier)
    {
        modifier = text.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => HotkeyModifiers.Control,
            "ALT" => HotkeyModifiers.Alt,
            "SHIFT" => HotkeyModifiers.Shift,
            "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };

        return modifier != HotkeyModifiers.None;
    }

    private static bool IsModifierKey(int key) => key is
        VirtualKeys.Shift or VirtualKeys.Control or VirtualKeys.Alt or
        VirtualKeys.LeftShift or VirtualKeys.RightShift or
        VirtualKeys.LeftControl or VirtualKeys.RightControl or
        VirtualKeys.LeftAlt or VirtualKeys.RightAlt or
        VirtualKeys.LeftWindows or VirtualKeys.RightWindows;
}

/// <summary>
/// Reads a hotkey from settings. An unreadable value falls back to the default gesture, because
/// one bad field should not cost the user the rest of their settings.
/// </summary>
internal sealed class HotkeyGestureJsonConverter : JsonConverter<HotkeyGesture>
{
    public override HotkeyGesture Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && HotkeyGesture.TryParse(reader.GetString(), out var gesture))
        {
            return gesture;
        }

        reader.TrySkip();
        return HotkeyGesture.Default;
    }

    public override void Write(Utf8JsonWriter writer, HotkeyGesture value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
