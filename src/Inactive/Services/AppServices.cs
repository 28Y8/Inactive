using Inactive.Updating;

namespace Inactive.Services;

/// <summary>What the windows and pages need from the app.</summary>
/// <param name="Settings">The settings in force.</param>
/// <param name="Locks">Starts locks.</param>
/// <param name="Hotkey">The lock hotkey.</param>
/// <param name="Updates">The updater.</param>
/// <param name="RestartToUpdate">Applies the ready update, unless the PC is locked.</param>
internal sealed record AppServices(
    SettingsService Settings,
    LockCoordinator Locks,
    GlobalHotkey Hotkey,
    IUpdateService Updates,
    Action RestartToUpdate);
