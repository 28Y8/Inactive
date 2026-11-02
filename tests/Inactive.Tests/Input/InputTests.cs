using Inactive.Core.Input;

namespace Inactive.Tests.Input;

public sealed class KeyNamesTests
{
    [Theory]
    [InlineData(VirtualKeys.RightShift, "Right Shift")]
    [InlineData(VirtualKeys.LeftShift, "Left Shift")]
    [InlineData(VirtualKeys.RightControl, "Right Ctrl")]
    [InlineData(VirtualKeys.RightAlt, "Right Alt")]
    [InlineData(VirtualKeys.Pause, "Pause")]
    [InlineData(VirtualKeys.ScrollLock, "Scroll Lock")]
    [InlineData(VirtualKeys.Escape, "Escape")]
    [InlineData(VirtualKeys.Enter, "Enter")]
    [InlineData(VirtualKeys.A, "A")]
    [InlineData(VirtualKeys.Z, "Z")]
    [InlineData(VirtualKeys.D0, "0")]
    [InlineData(VirtualKeys.D9, "9")]
    [InlineData(VirtualKeys.Numpad0, "Numpad 0")]
    [InlineData(VirtualKeys.Numpad9, "Numpad 9")]
    [InlineData(VirtualKeys.F1, "F1")]
    [InlineData(VirtualKeys.F12, "F12")]
    [InlineData(VirtualKeys.F24, "F24")]
    public void Names_common_keys(int key, string name) => Assert.Equal(name, KeyNames.GetName(key));

    [Theory]
    [InlineData(0x07, "Key 0x07")]
    [InlineData(0xFF, "Key 0xFF")]
    public void Unknown_keys_get_a_readable_fallback(int key, string name) =>
        Assert.Equal(name, KeyNames.GetName(key));

    [Fact]
    public void Every_key_can_be_found_again_by_its_name()
    {
        for (var key = 1; key < 0xFF; key++)
        {
            var name = KeyNames.GetName(key);

            Assert.True(KeyNames.TryGetKey(name, out var found), name);
            Assert.Equal(key, found);
        }
    }

    private static char NoLayout(int key) => '\0';

    [Theory]
    [InlineData(0xBA, ';', ";")]
    [InlineData(0xBA, '\u00DC', "\u00DC")]
    [InlineData(0xDE, '\u00C4', "\u00C4")]
    [InlineData(0xE2, '<', "<")]
    [InlineData(0xDF, '!', "!")]
    public void Punctuation_shows_what_the_layout_prints_on_the_key(int key, char onKey, string name) =>
        Assert.Equal(name, KeyNames.GetDisplayName(key, _ => onKey));

    [Theory]
    [InlineData(0xBA, ";")]
    [InlineData(0xE2, "Key 0xE2")]
    [InlineData(0xE3, "Key 0xE3")]
    public void Falls_back_when_the_layout_has_no_character(int key, string name) =>
        Assert.Equal(name, KeyNames.GetDisplayName(key, NoLayout));

    [Fact]
    public void A_layout_space_does_not_become_a_blank_name() =>
        Assert.Equal(";", KeyNames.GetDisplayName(0xBA, _ => ' '));

    [Theory]
    [InlineData(0xAD, "Volume Mute")]
    [InlineData(0xAF, "Volume Up")]
    [InlineData(0xB3, "Play or Pause")]
    [InlineData(0xA6, "Browser Back")]
    public void Media_and_browser_keys_have_display_names(int key, string name) =>
        Assert.Equal(name, KeyNames.GetDisplayName(key, NoLayout));

    [Theory]
    [InlineData(VirtualKeys.RightShift, "Right Shift")]
    [InlineData(VirtualKeys.A, "A")]
    [InlineData(VirtualKeys.F1, "F1")]
    public void Keys_with_a_fixed_name_ignore_the_layout(int key, string name) =>
        Assert.Equal(name, KeyNames.GetDisplayName(key, _ => 'X'));

    [Theory]
    [InlineData(0xAD, "Key 0xAD")]
    [InlineData(0xBA, ";")]
    public void Stored_names_stay_as_they_were(int key, string name) => Assert.Equal(name, KeyNames.GetName(key));

    [Fact]
    public void No_name_contains_a_plus_because_gestures_are_split_on_it()
    {
        for (var key = 1; key < 0xFF; key++)
        {
            Assert.DoesNotContain('+', KeyNames.GetName(key));
        }
    }

    [Theory]
    [InlineData("right shift", VirtualKeys.RightShift)]
    [InlineData("  F5  ", VirtualKeys.F1 + 4)]
    [InlineData("Esc", VirtualKeys.Escape)]
    [InlineData("Return", VirtualKeys.Enter)]
    [InlineData("Control", VirtualKeys.Control)]
    [InlineData("Del", VirtualKeys.Delete)]
    [InlineData("PgUp", VirtualKeys.PageUp)]
    [InlineData("PgDn", VirtualKeys.PageDown)]
    public void Finds_keys_by_name_and_alias(string name, int key)
    {
        Assert.True(KeyNames.TryGetKey(name, out var found));
        Assert.Equal(key, found);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nonsense")]
    public void Unknown_names_are_not_found(string? name) => Assert.False(KeyNames.TryGetKey(name, out _));

    [Theory]
    [InlineData(VirtualKeys.D0, false, '0')]
    [InlineData(VirtualKeys.D9, false, '9')]
    [InlineData(VirtualKeys.D0 + 5, true, '5')]
    [InlineData(VirtualKeys.Numpad0, false, '0')]
    [InlineData(VirtualKeys.Numpad9, false, '9')]
    [InlineData(VirtualKeys.Insert, false, '0')]
    [InlineData(VirtualKeys.End, false, '1')]
    [InlineData(VirtualKeys.DownArrow, false, '2')]
    [InlineData(VirtualKeys.PageDown, false, '3')]
    [InlineData(VirtualKeys.LeftArrow, false, '4')]
    [InlineData(VirtualKeys.Clear, false, '5')]
    [InlineData(VirtualKeys.RightArrow, false, '6')]
    [InlineData(VirtualKeys.Home, false, '7')]
    [InlineData(VirtualKeys.UpArrow, false, '8')]
    [InlineData(VirtualKeys.PageUp, false, '9')]
    public void Maps_pin_digits(int key, bool isExtended, char expected)
    {
        Assert.True(KeyNames.TryGetPinDigit(key, isExtended, out var digit));
        Assert.Equal(expected, digit);
    }

    [Theory]
    [InlineData(VirtualKeys.A, false)]
    [InlineData(VirtualKeys.Enter, false)]
    [InlineData(VirtualKeys.Insert, true)]
    [InlineData(VirtualKeys.End, true)]
    [InlineData(VirtualKeys.DownArrow, true)]
    [InlineData(VirtualKeys.PageUp, true)]
    public void Other_keys_are_not_pin_digits(int key, bool isExtended) =>
        Assert.False(KeyNames.TryGetPinDigit(key, isExtended, out _));
}

public sealed class UnlockKeyRulesTests
{
    [Theory]
    [InlineData(VirtualKeys.RightShift)]
    [InlineData(VirtualKeys.LeftShift)]
    [InlineData(VirtualKeys.RightControl)]
    [InlineData(VirtualKeys.RightAlt)]
    [InlineData(VirtualKeys.Pause)]
    [InlineData(VirtualKeys.ScrollLock)]
    [InlineData(VirtualKeys.F1)]
    [InlineData(VirtualKeys.F24)]
    public void Accepts_good_keys(int key) => Assert.Equal(UnlockKeyVerdict.Allowed, UnlockKeyRules.Evaluate(key));

    [Theory]
    [InlineData(VirtualKeys.Enter)]
    [InlineData(VirtualKeys.Escape)]
    [InlineData(VirtualKeys.Tab)]
    [InlineData(VirtualKeys.Space)]
    [InlineData(VirtualKeys.Backspace)]
    [InlineData(VirtualKeys.D0)]
    [InlineData(VirtualKeys.D9)]
    [InlineData(VirtualKeys.Numpad0)]
    [InlineData(VirtualKeys.Numpad9)]
    [InlineData(VirtualKeys.LeftWindows)]
    [InlineData(VirtualKeys.RightWindows)]
    public void Refuses_keys_with_another_job(int key) =>
        Assert.Equal(UnlockKeyVerdict.Refused, UnlockKeyRules.Evaluate(key));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(0xFF)]
    [InlineData(256)]
    [InlineData(VirtualKeys.LeftMouseButton)]
    [InlineData(VirtualKeys.ExtraMouseButton2)]
    [InlineData(VirtualKeys.Shift)]
    [InlineData(VirtualKeys.Control)]
    [InlineData(VirtualKeys.Alt)]
    [InlineData(VirtualKeys.Packet)]
    [InlineData(VirtualKeys.ProcessKey)]
    public void Refuses_keys_the_hook_would_never_report(int key) =>
        Assert.Equal(UnlockKeyVerdict.Refused, UnlockKeyRules.Evaluate(key));

    [Theory]
    [InlineData(VirtualKeys.A)]
    [InlineData(VirtualKeys.L)]
    [InlineData(VirtualKeys.Z)]
    public void Warns_about_letters(int key) =>
        Assert.Equal(UnlockKeyVerdict.AllowedWithWarning, UnlockKeyRules.Evaluate(key));
}

public sealed class HotkeyGestureTests
{
    [Fact]
    public void Default_is_ctrl_alt_l()
    {
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyGesture.Default.Modifiers);
        Assert.Equal(VirtualKeys.L, HotkeyGesture.Default.Key);
        Assert.True(HotkeyGesture.Default.IsValid);
    }

    [Theory]
    [InlineData(HotkeyModifiers.Alt, 0x1)]
    [InlineData(HotkeyModifiers.Control, 0x2)]
    [InlineData(HotkeyModifiers.Shift, 0x4)]
    [InlineData(HotkeyModifiers.Windows, 0x8)]
    public void Modifier_values_match_register_hotkey(HotkeyModifiers modifier, int value) =>
        Assert.Equal(value, (int)modifier);

    [Theory]
    [InlineData("Ctrl+Alt+L", HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKeys.L)]
    [InlineData("Shift+F5", HotkeyModifiers.Shift, VirtualKeys.F1 + 4)]
    [InlineData("Win+Pause", HotkeyModifiers.Windows, VirtualKeys.Pause)]
    [InlineData("Ctrl+Shift+Alt+Win+K", HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Windows, 0x4B)]
    [InlineData("Ctrl+Numpad 5", HotkeyModifiers.Control, VirtualKeys.Numpad0 + 5)]
    [InlineData("Ctrl+Scroll Lock", HotkeyModifiers.Control, VirtualKeys.ScrollLock)]
    public void Parses_gestures(string text, HotkeyModifiers modifiers, int key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, key), gesture);
    }

    [Theory]
    [InlineData("ctrl+alt+l")]
    [InlineData("CTRL+ALT+L")]
    [InlineData("Ctrl + Alt + L")]
    [InlineData("  Ctrl+Alt+L  ")]
    [InlineData("Control+Alt+L")]
    [InlineData("L+Alt+Ctrl")]
    public void Parsing_is_forgiving(string text) =>
        Assert.Equal(HotkeyGesture.Default, HotkeyGesture.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("L")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+")]
    [InlineData("+L")]
    [InlineData("Ctrl+Ctrl+L")]
    [InlineData("Ctrl+K+L")]
    [InlineData("Ctrl+Nonsense")]
    [InlineData("Ctrl+Left Shift")]
    [InlineData("Ctrl+Right Alt")]
    [InlineData("Ctrl+Left Windows")]
    public void Rejects_bad_gestures(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(default, gesture);
    }

    [Fact]
    public void Parse_throws_on_a_bad_gesture() =>
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse("nope"));

    [Theory]
    [InlineData("Ctrl+Alt+L", "Ctrl + Alt + L")]
    [InlineData("Shift+F5", "Shift + F5")]
    [InlineData("Ctrl+Win+Pause", "Ctrl + Win + Pause")]
    public void Formats_for_storage_and_display(string stored, string display)
    {
        var gesture = HotkeyGesture.Parse(stored);

        Assert.Equal(stored, gesture.ToString());
        Assert.Equal(display, gesture.DisplayText);
    }

    [Fact]
    public void Modifiers_print_in_a_fixed_order()
    {
        var gesture = HotkeyGesture.Parse("Win+Shift+Alt+Ctrl+K");

        Assert.Equal("Ctrl+Alt+Shift+Win+K", gesture.ToString());
    }

    [Fact]
    public void Text_round_trips_for_every_valid_key()
    {
        for (var key = 1; key < 0xFF; key++)
        {
            var gesture = new HotkeyGesture(HotkeyModifiers.Control, key);
            if (gesture.IsValid)
            {
                Assert.Equal(gesture, HotkeyGesture.Parse(gesture.ToString()));
            }
        }
    }

    [Fact]
    public void A_key_with_a_hex_name_survives_a_restart()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0xE2);

        Assert.Equal("Ctrl+Alt+Key 0xE2", gesture.ToString());
        Assert.Equal(gesture, HotkeyGesture.Parse(gesture.ToString()));
    }

    [Theory]
    [InlineData(HotkeyModifiers.Shift, VirtualKeys.L)]
    [InlineData(HotkeyModifiers.Shift, VirtualKeys.D0)]
    [InlineData(HotkeyModifiers.Shift, 0xBA)]
    [InlineData(HotkeyModifiers.Shift, 0xE2)]
    public void Shift_with_a_typing_key_types_a_character(HotkeyModifiers modifiers, int key) =>
        Assert.True(new HotkeyGesture(modifiers, key).TypesCharacter);

    [Theory]
    [InlineData(HotkeyModifiers.Shift, VirtualKeys.F1)]
    [InlineData(HotkeyModifiers.Shift, VirtualKeys.Pause)]
    [InlineData(HotkeyModifiers.Shift | HotkeyModifiers.Control, VirtualKeys.L)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKeys.L)]
    [InlineData(HotkeyModifiers.Windows, VirtualKeys.L)]
    public void Other_gestures_do_not_type_a_character(HotkeyModifiers modifiers, int key) =>
        Assert.False(new HotkeyGesture(modifiers, key).TypesCharacter);

    [Fact]
    public void Shift_with_a_letter_still_loads_from_settings()
    {
        Assert.True(HotkeyGesture.TryParse("Shift+L", out var gesture));
        Assert.True(gesture.IsValid);
    }

    [Theory]
    [InlineData(HotkeyModifiers.None, VirtualKeys.L)]
    [InlineData(HotkeyModifiers.Control, 0)]
    [InlineData(HotkeyModifiers.Control, 256)]
    [InlineData(HotkeyModifiers.Control, VirtualKeys.LeftControl)]
    public void Invalid_gestures_say_so(HotkeyModifiers modifiers, int key) =>
        Assert.False(new HotkeyGesture(modifiers, key).IsValid);
}
