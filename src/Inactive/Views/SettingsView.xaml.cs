using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using Inactive.Core.Input;
using Inactive.Core.Logging;
using Inactive.Core.Security;
using Inactive.Dialogs;
using Inactive.Services;
using Inactive.Startup;
using Inactive.Updating;

namespace Inactive.Views;

/// <summary>The settings page. Every change takes effect and is saved right away.</summary>
internal sealed partial class SettingsView : UserControl
{
    private const string DevelopmentUpdatesText = "Updates are off in development builds";
    private const string AutoUpdateOnText = "Download new versions in the background and install them on exit or next launch.";
    private const string LetterKeyWarning = "Letter keys are easy to press by accident, and anyone nearby could press one.";
    private const string DamagedPinWarning = "The saved PIN can't be read. Set a new one to keep using a PIN.";
    private const string SaveFailedMessage = "Your changes couldn't be saved. They'll last until Inactive closes.";
    private const string StartupFailedMessage = "Windows didn't allow this change.";

    private static readonly TimeSpan MessageSaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly AppServices _services;
    private readonly DispatcherTimer _messageTimer = new();
    private string _updateStatus;
    private bool _statusNamesReadyUpdate;
    private bool _isRefreshing;

    public SettingsView(AppServices services)
    {
        InitializeComponent();

        _services = services;
        _updateStatus = InstalledStatus;

        _messageTimer.Interval = MessageSaveDelay;
        _messageTimer.Tick += (_, _) => CommitMessage();

        _services.Settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public event EventHandler? BackRequested;

    public void Refresh()
    {
        _isRefreshing = true;

        var settings = _services.Settings.Current;

        SaveNotice.Show(_services.Settings.LastSaveFailed ? SaveFailedMessage : null);

        UnlockKeyText.Text = KeyNames.GetDisplayName(settings.UnlockKey);
        UnlockKeyNotice.Show(
            UnlockKeyRules.Evaluate(settings.UnlockKey) == UnlockKeyVerdict.AllowedWithWarning ? LetterKeyWarning : null);

        RefreshPin();
        RefreshHotkey();

        DisplayOffToggle.IsChecked = settings.AllowDisplayOff;
        ClockToggle.IsChecked = settings.ShowClock;
        HintToggle.IsChecked = settings.ShowUnlockHint;
        StartupToggle.IsChecked = StartupRegistration.IsEnabled();

        if (!MessageBox.IsKeyboardFocused && MessageBox.Text != settings.CustomMessage)
        {
            MessageBox.Text = settings.CustomMessage;
        }

        var updates = _services.Updates;
        AutoUpdateToggle.IsChecked = settings.AutoUpdate;
        AutoUpdateToggle.IsEnabled = updates.IsEnabled;
        AutoUpdateRow.Description = updates.IsEnabled ? AutoUpdateOnText : DevelopmentUpdatesText;
        CheckUpdatesButton.IsEnabled = updates.IsEnabled;
        RefreshUpdateStatus();

        VersionText.Text = $"Version {updates.CurrentVersion}";

        _isRefreshing = false;
    }

    public void FocusFirstControl() =>
        Dispatcher.BeginInvoke(() => BackButton.Focus(), DispatcherPriority.Input);

    private string InstalledStatus => $"Version {_services.Updates.CurrentVersion} is installed.";

    // A check can find an update at any time, and a failed download can take one away, so the line
    // follows the updater and not only the last button press.
    private void RefreshUpdateStatus()
    {
        var ready = _services.Updates.ReadyVersion;
        if (ready is not null)
        {
            _updateStatus = $"Inactive {ready} is ready. Restart to install it.";
            _statusNamesReadyUpdate = true;
        }
        else if (_statusNamesReadyUpdate)
        {
            _updateStatus = InstalledStatus;
            _statusNamesReadyUpdate = false;
        }

        CheckUpdatesRow.Description = _updateStatus;
    }

    private void RefreshPin()
    {
        var pin = _services.Settings.Current.Pin;
        var isUsable = PinHasher.IsUsable(pin);
        var isDamaged = pin is not null && !isUsable;

        PinRow.Description = isUsable
            ? string.Create(CultureInfo.CurrentCulture, $"On. {pin!.Length} digits after the unlock key.")
            : "Ask for a PIN after the unlock key.";

        SetPinButton.Content = isDamaged ? "Set a new PIN" : "Set PIN";
        SetPinButton.Visibility = isUsable ? Visibility.Collapsed : Visibility.Visible;
        ChangePinButton.Visibility = isUsable ? Visibility.Visible : Visibility.Collapsed;
        RemovePinButton.Visibility = isUsable ? Visibility.Visible : Visibility.Collapsed;

        PinNotice.Show(isDamaged ? DamagedPinWarning : null);
    }

    private void RefreshHotkey()
    {
        var gesture = _services.Settings.Current.LockHotkey;

        HotkeyRow.Description = gesture is { } active
            ? $"Lock from anywhere with {active.DisplayText}."
            : "Off. You can still lock from here or from the tray.";
        HotkeyOffButton.Visibility = gesture is null ? Visibility.Collapsed : Visibility.Visible;

        HotkeyNotice.Show(_services.Hotkey.HasFailed
            ? $"{gesture?.DisplayText} is taken by another app. Choose a different hotkey."
            : null);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnChangeUnlockKeyClick(object sender, RoutedEventArgs e)
    {
        var dialog = KeyRecorderDialog.ForUnlockKey();
        if (DialogHost.ShowModal(dialog) && dialog.RecordedKey is { } key)
        {
            _services.Settings.Update(settings => settings with { UnlockKey = key });
        }
    }

    private void OnSetPinClick(object sender, RoutedEventArgs e) => EditPin(PinDialogMode.Set);

    private void OnChangePinClick(object sender, RoutedEventArgs e) => EditPin(PinDialogMode.Change);

    private void OnRemovePinClick(object sender, RoutedEventArgs e) => EditPin(PinDialogMode.Remove);

    private void EditPin(PinDialogMode mode)
    {
        var dialog = new PinDialog(mode, _services.Settings.Current.Pin);
        if (DialogHost.ShowModal(dialog))
        {
            _services.Settings.Update(settings => settings.WithPin(dialog.Result));
        }
    }

    private void OnChangeHotkeyClick(object sender, RoutedEventArgs e)
    {
        _services.Hotkey.Suspend();

        try
        {
            var dialog = KeyRecorderDialog.ForHotkey();
            if (DialogHost.ShowModal(dialog) && dialog.RecordedGesture is { } gesture)
            {
                _services.Settings.Update(settings => settings with { LockHotkey = gesture });
            }
        }
        finally
        {
            _services.Hotkey.Resume();
        }

        Refresh();
    }

    private void OnHotkeyOffClick(object sender, RoutedEventArgs e) =>
        _services.Settings.Update(settings => settings with { LockHotkey = null });

    private void OnDisplayOffClick(object sender, RoutedEventArgs e)
    {
        var value = DisplayOffToggle.IsChecked == true;
        _services.Settings.Update(settings => settings with { AllowDisplayOff = value });
    }

    private void OnClockClick(object sender, RoutedEventArgs e)
    {
        var value = ClockToggle.IsChecked == true;
        _services.Settings.Update(settings => settings with { ShowClock = value });
    }

    private void OnHintClick(object sender, RoutedEventArgs e)
    {
        var value = HintToggle.IsChecked == true;
        _services.Settings.Update(settings => settings with { ShowUnlockHint = value });
    }

    private void OnAutoUpdateClick(object sender, RoutedEventArgs e)
    {
        var value = AutoUpdateToggle.IsChecked == true;
        _services.Settings.Update(settings => settings with { AutoUpdate = value });
    }

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        var value = StartupToggle.IsChecked == true;

        if (StartupRegistration.TrySet(value))
        {
            StartupNotice.Show(null);
            _services.Settings.Update(settings => settings with { StartWithWindows = value });
        }
        else
        {
            StartupToggle.IsChecked = !value;
            StartupNotice.Show(StartupFailedMessage);
        }
    }

    private void OnMessageChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isRefreshing)
        {
            _messageTimer.Stop();
            _messageTimer.Start();
        }
    }

    private void OnMessageLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitMessage();

    private void CommitMessage()
    {
        _messageTimer.Stop();

        var message = MessageBox.Text.Trim();
        _services.Settings.Update(settings => settings with { CustomMessage = message });
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        ShowUpdateStatus("Checking for updates");

        var result = await _services.Updates.CheckNowAsync();

        if (result == UpdateCheckResult.UpdateReady)
        {
            RefreshUpdateStatus();
        }
        else
        {
            ShowUpdateStatus(result switch
            {
                UpdateCheckResult.Disabled => DevelopmentUpdatesText,
                UpdateCheckResult.UpToDate => "You're up to date.",
                _ => "Couldn't check for updates. Try again later.",
            });
        }

        CheckUpdatesButton.IsEnabled = _services.Updates.IsEnabled;
    }

    private void ShowUpdateStatus(string status)
    {
        _updateStatus = status;
        _statusNamesReadyUpdate = false;
        CheckUpdatesRow.Description = status;
    }

    private void OnLinkRequested(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;

        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            AppLog.Error("A link could not be opened.", exception);
        }
    }
}
