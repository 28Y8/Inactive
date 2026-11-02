using System.IO;
using System.Security;
using Inactive.Core.Logging;
using Microsoft.Win32;
using Velopack.Locators;

namespace Inactive.Startup;

/// <summary>
/// Starts the app with Windows through the per-user Run key. This also runs from the Velopack
/// uninstall hook, so it must stay free of WPF types.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Inactive";
    private const string ExecutableName = "Inactive.exe";

    /// <summary>Adds or removes the Run entry. Returns false when the registry refused.</summary>
    public static bool TrySet(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppLog.Error("The Windows startup entry could not be changed.", e);
            return false;
        }
    }

    /// <summary>True when Windows will start the app, which is what the Settings toggle shows.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppLog.Error("The Windows startup entry could not be read.", e);
            return false;
        }
    }

    /// <summary>
    /// Points the entry at this install again, in case the app moved. Does nothing for an uninstalled
    /// build, which would otherwise take the entry over from the installed copy.
    /// </summary>
    public static void RefreshIfInstalled()
    {
        if (VelopackLocator.IsCurrentSet && VelopackLocator.Current.RootAppDir is not null)
        {
            TrySet(enabled: true);
        }
    }

    public static void RemoveQuietly() => TrySet(enabled: false);

    private static string BuildCommand() => $"\"{GetStablePath()}\" --background";

    // Velopack keeps a stub in the root folder that outlives updates, while the real executable
    // moves with each version. A development build has no stub, so it registers itself.
    private static string GetStablePath()
    {
        var root = VelopackLocator.IsCurrentSet ? VelopackLocator.Current.RootAppDir : null;
        return root is null
            ? Environment.ProcessPath ?? ExecutableName
            : Path.Combine(root, ExecutableName);
    }
}
