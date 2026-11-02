using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Inactive.Controls;
using Inactive.Core.Security;

namespace Inactive.Dialogs;

internal enum PinDialogMode
{
    Set,
    Change,
    Remove,
}

/// <summary>Sets, changes or removes the PIN. Hashing runs off the UI thread because PBKDF2 is slow.</summary>
internal sealed partial class PinDialog : Window
{
    private readonly PinDialogMode _mode;
    private readonly PinCredential? _existing;
    private bool _isWorking;

    /// <param name="mode">What to do.</param>
    /// <param name="existing">The stored PIN, which <see cref="PinDialogMode.Change"/> and <see cref="PinDialogMode.Remove"/> verify first.</param>
    public PinDialog(PinDialogMode mode, PinCredential? existing)
    {
        InitializeComponent();

        _mode = mode;
        _existing = existing;

        var (heading, instruction, submit) = mode switch
        {
            PinDialogMode.Change => ("Change PIN", "Use 4 to 8 digits.", "Change PIN"),
            PinDialogMode.Remove => ("Remove PIN", "After this, the unlock key alone ends the lock.", "Remove PIN"),
            _ => ("Set a PIN", "Use 4 to 8 digits. You'll enter it after pressing the unlock key.", "Set PIN"),
        };

        HeadingText.Text = heading;
        InstructionText.Text = instruction;
        SubmitButton.Content = submit;
        AutomationProperties.SetName(SubmitButton, submit);

        CurrentPanel.Visibility = NeedsCurrentPin ? Visibility.Visible : Visibility.Collapsed;
        NewPanel.Visibility = mode == PinDialogMode.Remove ? Visibility.Collapsed : Visibility.Visible;

        Loaded += (_, _) =>
        {
            Activate();
            (NeedsCurrentPin ? CurrentBox : NewBox).Focus();
        };
    }

    /// <summary>The new PIN, or null when the PIN was removed.</summary>
    public PinCredential? Result { get; private set; }

    private bool NeedsCurrentPin => _mode != PinDialogMode.Set;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Setting DialogResult after the window has closed would throw.
        e.Cancel = _isWorking;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        CurrentBox.Clear();
        NewBox.Clear();
        ConfirmBox.Clear();
        base.OnClosed(e);
    }

    private void OnPinTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    private async void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        ShowError(null);

        var current = CurrentBox.Password;
        var pin = NewBox.Password;
        var isSettingPin = _mode != PinDialogMode.Remove;

        if (isSettingPin && !PinHasher.IsValidPin(pin))
        {
            ShowError("Use 4 to 8 digits.");
            NewBox.Focus();
            return;
        }

        if (isSettingPin && pin != ConfirmBox.Password)
        {
            ShowError("The two PINs don't match.");
            ConfirmBox.Clear();
            ConfirmBox.Focus();
            return;
        }

        SetWorking(true);

        var existing = _existing;
        var isCurrentCorrect = !NeedsCurrentPin
            || (existing is not null && await Task.Run(() => PinHasher.Verify(current, existing)));

        if (!isCurrentCorrect)
        {
            SetWorking(false);
            ShowError("That PIN isn't right.");
            CurrentBox.Clear();
            CurrentBox.Focus();
            return;
        }

        Result = isSettingPin ? await Task.Run(() => PinHasher.Create(pin)) : null;

        SetWorking(false);
        DialogResult = true;
    }

    private void SetWorking(bool isWorking)
    {
        _isWorking = isWorking;
        SubmitButton.IsEnabled = !isWorking;
        CancelButton.IsEnabled = !isWorking;
    }

    private void ShowError(string? message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;

        if (message is not null)
        {
            LiveRegion.Announce(ErrorText);
        }
    }
}
