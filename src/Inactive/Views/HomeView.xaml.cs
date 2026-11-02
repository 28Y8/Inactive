using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Inactive.Controls;
using Inactive.Core.Input;
using Inactive.Core.Settings;
using Inactive.Services;

namespace Inactive.Views;

/// <summary>The home page: the lock button, the current unlock key and the screen style.</summary>
internal sealed partial class HomeView : UserControl
{
    private readonly AppServices _services;
    private bool _isRefreshing;

    public HomeView(AppServices services)
    {
        InitializeComponent();

        _services = services;
        _services.Settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public event EventHandler? SettingsRequested;

    public void Refresh()
    {
        _isRefreshing = true;

        var settings = _services.Settings.Current;
        var unlockKey = KeyNames.GetDisplayName(settings.UnlockKey);

        BodyText.Text = settings.Pin is null
            ? $"Your PC stays awake and everything keeps running. Nobody can use it until you press {unlockKey}."
            : $"Your PC stays awake and everything keeps running. Nobody can use it until you press {unlockKey} and enter your PIN.";

        HotkeyText.Text = _services.Hotkey.ActiveGesture is { } gesture ? $"or press {gesture.DisplayText}" : string.Empty;

        VisibleOption.IsChecked = settings.ScreenStyle == ScreenStyle.Visible;
        DimmedOption.IsChecked = settings.ScreenStyle == ScreenStyle.Dimmed;
        CoveredOption.IsChecked = settings.ScreenStyle == ScreenStyle.Covered;

        var ready = _services.Updates.ReadyVersion;
        UpdateNotice.Show(ready is null ? null : $"Inactive {ready} is ready");

        _isRefreshing = false;
    }

    /// <summary>Shows why the last lock failed, or clears the message when given null.</summary>
    public void ShowLockNotice(string? message) => LockNotice.Show(message, NoticeSeverity.Critical);

    /// <summary>Says that the saved settings were lost, or clears the message when given null.</summary>
    public void ShowSettingsNotice(string? message) => SettingsNotice.Show(message);

    public void FocusFirstControl() =>
        Dispatcher.BeginInvoke(() => LockButton.Focus(), DispatcherPriority.Input);

    private void OnLockClick(object sender, RoutedEventArgs e) => _services.Locks.RequestLock();

    private void OnRestartClick(object sender, RoutedEventArgs e) => _services.RestartToUpdate();

    private void OnSettingsClick(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnStyleChecked(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        var style = sender == VisibleOption ? ScreenStyle.Visible
            : sender == CoveredOption ? ScreenStyle.Covered
            : ScreenStyle.Dimmed;

        _services.Settings.Update(settings => settings with { ScreenStyle = style });
    }
}
