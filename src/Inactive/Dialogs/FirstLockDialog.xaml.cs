using System.Windows;
using Inactive.Core.Input;
using Inactive.Core.Settings;

namespace Inactive.Dialogs;

/// <summary>Shown before the first lock ever, so that nobody gets locked without knowing the way out.</summary>
internal sealed partial class FirstLockDialog : Window
{
    public FirstLockDialog(AppSettings settings)
    {
        InitializeComponent();

        UnlockKeyText.Text = KeyNames.GetDisplayName(settings.UnlockKey);
        PinText.Visibility = settings.Pin is null ? Visibility.Collapsed : Visibility.Visible;

        Loaded += (_, _) =>
        {
            Activate();
            LockButton.Focus();
        };
    }

    private void OnLockClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
