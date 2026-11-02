using System.Windows;

namespace Inactive.Dialogs;

/// <summary>Shown before locking in tablet mode. Cancel is the default, because a wrong yes is a lockout.</summary>
internal sealed partial class NoKeyboardDialog : Window
{
    public NoKeyboardDialog()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Activate();
            CancelButton.Focus();
        };
    }

    private void OnLockClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
