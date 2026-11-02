using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using Inactive.Core.Logging;
using Inactive.Native;

namespace Inactive;

/// <summary>The tray icon: a WinForms NotifyIcon with a WPF menu, so the menu follows the Fluent theme.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int ToastMilliseconds = 6000;
    private const string ReadyTip = "Inactive: ready to lock";
    private const string LockedTip = "Inactive: this PC is locked";

    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _idleIcon;
    private readonly System.Drawing.Icon _lockedIcon;
    private readonly ContextMenu _menu;
    private Action? _toastAction;
    private bool _isLocked;

    public TrayIcon()
    {
        _idleIcon = LoadIcon("Inactive.ico");
        _lockedIcon = LoadIcon("InactiveLocked.ico");
        _menu = BuildMenu();

        _icon = new System.Windows.Forms.NotifyIcon { Icon = _idleIcon, Text = ReadyTip, Visible = true };
        _icon.MouseUp += OnMouseUp;
        _icon.BalloonTipClicked += OnToastClicked;
        _icon.BalloonTipClosed += OnToastClosed;
    }

    public event EventHandler? LockRequested;

    public event EventHandler? OpenRequested;

    public event EventHandler? ToggleWindowRequested;

    public event EventHandler? CheckForUpdatesRequested;

    public event EventHandler? ExitRequested;

    public void ShowLocked(bool isLocked)
    {
        _isLocked = isLocked;

        // A touch can sometimes reach the taskbar above the overlay, and the menu has no place there.
        if (isLocked)
        {
            _menu.IsOpen = false;
        }

        _icon.Icon = isLocked ? _lockedIcon : _idleIcon;
        _icon.Text = isLocked ? LockedTip : ReadyTip;
    }

    /// <summary>Shows a toast. <paramref name="onClick"/> runs if the person selects it.</summary>
    public void ShowToast(string title, string text, Action? onClick = null)
    {
        _toastAction = onClick;
        _icon.ShowBalloonTip(ToastMilliseconds, title, text, System.Windows.Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        // Hiding first removes the icon right away instead of leaving a dead one until the mouse passes over it.
        _icon.Visible = false;
        _icon.Dispose();
        _idleIcon.Dispose();
        _lockedIcon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon(string fileName)
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{fileName}"));
            using var stream = resource.Stream;
            return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        }
        catch (IOException e)
        {
            AppLog.Error("A tray icon could not be loaded.", e);
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.Opened += OnMenuOpened;

        menu.Items.Add(CreateItem("Lock now", () => LockRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(CreateItem("Open Inactive", () => OpenRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Check for updates", () => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Exit", () => ExitRequested?.Invoke(this, EventArgs.Empty)));

        return menu;
    }

    private static MenuItem CreateItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnMouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        switch (e.Button)
        {
            case System.Windows.Forms.MouseButtons.Left:
                ToggleWindowRequested?.Invoke(this, EventArgs.Empty);
                break;

            case System.Windows.Forms.MouseButtons.Right when !_isLocked:
                _menu.IsOpen = true;
                break;
        }
    }

    // A menu with no owning window only closes on an outside click if its popup holds the foreground.
    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual(_menu) is HwndSource source)
        {
            NativeMethods.SetForegroundWindow(source.Handle);
        }
    }

    private void OnToastClicked(object? sender, EventArgs e)
    {
        var action = _toastAction;
        _toastAction = null;
        action?.Invoke();
    }

    private void OnToastClosed(object? sender, EventArgs e) => _toastAction = null;
}
