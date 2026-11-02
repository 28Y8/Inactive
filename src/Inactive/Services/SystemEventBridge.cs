using Inactive.Core.Locking;
using Microsoft.Win32;

namespace Inactive.Services;

/// <summary>Connects Windows session, power and display events to the lock.</summary>
internal sealed class SystemEventBridge : IDisposable
{
    private readonly LockController _engine;
    private readonly Action _environmentChanged;

    /// <param name="engine">The lock to release or reassert.</param>
    /// <param name="environmentChanged">Called after a resume or a display change.</param>
    public SystemEventBridge(LockController engine, Action environmentChanged)
    {
        _engine = engine;
        _environmentChanged = environmentChanged;

        // SystemEvents keeps the context of the subscribing thread and blocks on it for every event,
        // which would put the release behind the UI thread. With the UI context out of the way the
        // handlers run on the system events thread, so signing in ends the lock even with a hung
        // UI. Everything they call is safe from any thread.
        var uiContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.SessionEnding += OnSessionEnding;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(uiContext);
        }
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // The person just proved who they are to Windows, which outranks any key or PIN of ours.
        if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _engine.Release(UnlockReason.SessionUnlocked);
        }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => _engine.Release(UnlockReason.Shutdown);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Reassert();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Reassert();

    private void Reassert()
    {
        _engine.Reassert();
        _environmentChanged();
    }
}
