using System.Windows;

namespace Inactive.Dialogs;

internal static class DialogHost
{
    /// <summary>
    /// Shows a dialog over the main window, or centred on the screen and on top of everything when
    /// the app is in the tray, as it is after the hotkey or a tray command.
    /// </summary>
    public static bool ShowModal(Window dialog)
    {
        var owner = Application.Current.MainWindow;
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
        }

        return dialog.ShowDialog() == true;
    }
}
