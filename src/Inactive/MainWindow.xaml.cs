using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Inactive.Core.Settings;
using Inactive.Services;
using Inactive.Views;

namespace Inactive;

/// <summary>The app window. It has two pages: home and settings.</summary>
internal sealed partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly HomeView _home;
    private SettingsView? _settings;

    public MainWindow(AppServices services)
    {
        InitializeComponent();

        _services = services;
        _home = new HomeView(services);
        _home.SettingsRequested += (_, _) => ShowSettings();
        PageHost.Content = _home;

        // Keeps the window on screen at high scale on a small display. The pages scroll.
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
    }

    /// <summary>Set when the app is really ending, so that closing no longer hides to the tray.</summary>
    public bool AllowClose { get; set; }

    /// <summary>The window was closed by the person and is now in the tray.</summary>
    public event EventHandler? HiddenToTray;

    public void ShowAndActivate()
    {
        _home.Refresh();
        _settings?.Refresh();

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    public void ShowLockFailure(string message)
    {
        ShowHome();
        _home.ShowLockNotice(message);
    }

    public void ShowEngineFault()
    {
        ShowHome();
        _home.ShowLockNotice("Inactive ran into a problem and unlocked your PC. Try locking again.");
    }

    public void ClearNotices() => _home.ShowLockNotice(null);

    /// <summary>The unlock key or PIN changed, so advice about them no longer applies.</summary>
    public void ClearUnlockNotices()
    {
        _home.ShowLockNotice(null);
        _home.ShowSettingsNotice(null);
    }

    public void ShowSettingsLoadProblem(SettingsLoadResult result) => _home.ShowSettingsNotice(result switch
    {
        SettingsLoadResult.Corrupt => "Your settings couldn't be read and were reset. Check your unlock key and PIN.",
        SettingsLoadResult.ReadFailed => "Your settings couldn't be read, so defaults are in use. Restart Inactive to try again.",
        _ => null,
    });

    public void ShowUpdateState()
    {
        _home.Refresh();
        _settings?.Refresh();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            ShowHome();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
        }

        base.OnClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var isBack = e.Key is Key.Escape or Key.BrowserBack || (e.Key == Key.System && e.SystemKey == Key.Left);
        if (isBack && PageHost.Content == _settings)
        {
            ShowHome();
            e.Handled = true;
        }
    }

    private void ShowSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsView(_services);
            _settings.BackRequested += (_, _) => ShowHome();
        }

        _settings.Refresh();
        PageHost.Content = _settings;
        _settings.FocusFirstControl();
    }

    private void ShowHome()
    {
        _home.Refresh();
        PageHost.Content = _home;
        _home.FocusFirstControl();
    }
}
