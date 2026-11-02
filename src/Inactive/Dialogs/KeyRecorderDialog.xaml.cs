using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Inactive.Controls;
using Inactive.Core.Input;

namespace Inactive.Dialogs;

/// <summary>Records one key press: the unlock key on its own, or a hotkey with modifiers.</summary>
internal sealed partial class KeyRecorderDialog : Window
{
    private const string Waiting = "Waiting";

    private readonly bool _recordsHotkey;

    private KeyRecorderDialog(bool recordsHotkey, string heading, string instruction, string confirmLabel)
    {
        InitializeComponent();

        _recordsHotkey = recordsHotkey;
        HeadingText.Text = heading;
        InstructionText.Text = instruction;
        RecordedText.Text = Waiting;
        ConfirmButton.Content = confirmLabel;
        AutomationProperties.SetName(ConfirmButton, confirmLabel);

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    /// <summary>The chosen unlock key, once the dialog was confirmed.</summary>
    public int? RecordedKey { get; private set; }

    /// <summary>The chosen hotkey, once the dialog was confirmed.</summary>
    public HotkeyGesture? RecordedGesture { get; private set; }

    public static KeyRecorderDialog ForUnlockKey() => new(
        recordsHotkey: false,
        "Choose an unlock key",
        "Press the key you want to use. Press Enter to confirm or Esc to cancel.",
        "Use this key");

    public static KeyRecorderDialog ForHotkey() => new(
        recordsHotkey: true,
        "Choose a lock hotkey",
        "Press the shortcut you want to use. Press Enter to confirm or Esc to cancel.",
        "Use this hotkey");

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        e.Handled = true;

        if (e.IsRepeat)
        {
            return;
        }

        var key = ToVirtualKey(e);
        var hasModifiers = Keyboard.Modifiers != ModifierKeys.None;

        if (key == VirtualKeys.Escape && !hasModifiers)
        {
            DialogResult = false;
        }
        else if (key == VirtualKeys.Enter && !hasModifiers && ConfirmButton.IsEnabled)
        {
            DialogResult = true;
        }
        else if (_recordsHotkey)
        {
            RecordHotkey(key);
        }
        else
        {
            RecordUnlockKey(key);
        }
    }

    private static int ToVirtualKey(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };

        return KeyInterop.VirtualKeyFromKey(key);
    }

    private static bool IsModifierKey(int key) => key is
        VirtualKeys.LeftShift or VirtualKeys.RightShift or
        VirtualKeys.LeftControl or VirtualKeys.RightControl or
        VirtualKeys.LeftAlt or VirtualKeys.RightAlt or
        VirtualKeys.LeftWindows or VirtualKeys.RightWindows;

    private void RecordUnlockKey(int key)
    {
        var name = KeyNames.GetDisplayName(key);

        switch (UnlockKeyRules.Evaluate(key))
        {
            case UnlockKeyVerdict.Refused:
                Reject($"{name} can't be the unlock key. Try Right Shift, Pause or an F key.");
                break;

            case UnlockKeyVerdict.AllowedWithWarning:
                Accept(name, "Letter keys are easy to press by accident, and anyone nearby could press one.");
                RecordedKey = key;
                break;

            default:
                Accept(name, string.Empty);
                RecordedKey = key;
                break;
        }
    }

    private void RecordHotkey(int key)
    {
        // A modifier on its own is the first half of a shortcut.
        if (IsModifierKey(key))
        {
            return;
        }

        var gesture = new HotkeyGesture((HotkeyModifiers)Keyboard.Modifiers, key);
        if (!gesture.IsValid)
        {
            Reject("Add Ctrl, Alt, Shift or Win, so that the shortcut can't be pressed by accident.");
        }
        else if (gesture.TypesCharacter)
        {
            Reject("Shift with this key types a character. Add Ctrl, Alt or Win.");
        }
        else
        {
            Accept(gesture.DisplayText, string.Empty);
            RecordedGesture = gesture;
        }
    }

    private void Accept(string text, string feedback)
    {
        RecordedText.Text = text;
        FeedbackText.Text = feedback;
        ConfirmButton.IsEnabled = true;
        Announce(feedback);
    }

    private void Reject(string feedback)
    {
        RecordedKey = null;
        RecordedGesture = null;
        RecordedText.Text = Waiting;
        FeedbackText.Text = feedback;
        ConfirmButton.IsEnabled = false;
        Announce(feedback);
    }

    private void Announce(string feedback)
    {
        LiveRegion.Announce(RecordedText);

        if (feedback.Length > 0)
        {
            LiveRegion.Announce(FeedbackText);
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
