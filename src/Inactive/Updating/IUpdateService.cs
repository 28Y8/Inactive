namespace Inactive.Updating;

/// <summary>What the UI needs from the updater.</summary>
public interface IUpdateService
{
    /// <summary>False in development builds, where the app was not installed by Velopack.</summary>
    bool IsEnabled { get; }

    string CurrentVersion { get; }

    /// <summary>The downloaded version waiting to be applied. Null if there is none.</summary>
    string? ReadyVersion { get; }

    /// <summary>Raised when <see cref="ReadyVersion"/> changes, on a background thread.</summary>
    event EventHandler? StateChanged;

    /// <summary>Starts the background schedule. Does nothing when updates are disabled.</summary>
    void Start();

    /// <summary>Checks now and downloads whatever is new. Never throws.</summary>
    Task<UpdateCheckResult> CheckNowAsync();

    /// <summary>Closes the app, applies the ready update and starts again. Refused while locked.</summary>
    void RestartToApply();

    /// <summary>Hands the ready update to the updater for when this process ends. Refused while locked.</summary>
    void ApplyOnExit();
}

public enum UpdateCheckResult
{
    Disabled,
    UpToDate,
    UpdateReady,
    Failed,
}
