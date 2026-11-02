using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using Inactive.Core.Input;
using Inactive.Core.Locking;
using Inactive.Core.Logging;
using Inactive.Core.Settings;

namespace Inactive.Overlay;

/// <summary>Owns one overlay per monitor for the length of a lock. Used from the UI thread.</summary>
internal sealed class OverlayManager
{
    private const string EnterPinTitle = "Enter your PIN";
    private const string CooldownTitle = "Too many wrong tries";
    private const string CancelHint = "Press Esc to cancel";
    private const string CheckingStatus = "Checking your PIN";
    private const string RejectedStatus = "That PIN isn't right. Try again.";

    private const int TopmostEverySeconds = 2;

    private readonly Func<TimeSpan> _cooldownRemaining;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _retireFallback = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ElevatedFocusWatch _elevatedFocus = new();

    private List<OverlayWindow> _windows = [];

    // Old windows from a rebuild, still covering the screen until their replacements have drawn.
    private List<OverlayWindow> _retiring = [];
    private OverlayContent? _content;
    private LockPhase _phase = LockPhase.Locked;
    private int _pinEntered;
    private int _pinLength;
    private bool _pinWasRejected;
    private int _lastMinute = -1;
    private int _tick;
    private bool _loggedFocusRefusal;

    /// <param name="cooldownRemaining">Reads the time left in the PIN cooldown.</param>
    public OverlayManager(Func<TimeSpan> cooldownRemaining)
    {
        _cooldownRemaining = cooldownRemaining;
        _timer.Tick += OnTick;
        _retireFallback.Tick += (_, _) => CloseRetiring();
    }

    /// <summary>Covers every monitor. If this throws, nothing stays on screen.</summary>
    public void Show(AppSettings settings)
    {
        _content = CreateContent(settings);
        _phase = LockPhase.Locked;
        _pinEntered = 0;
        _pinLength = 0;
        _pinWasRejected = false;
        _lastMinute = -1;
        _loggedFocusRefusal = false;
        _elevatedFocus.Reset();

        _windows = CreateWindows(_content, fadesIn: true);
        UpdateClock();
        _timer.Start();
    }

    public void Close()
    {
        _timer.Stop();
        _content = null;
        CloseRetiring();
        CloseAll(_windows);
        _windows = [];
    }

    /// <summary>
    /// Rebuilds the windows after a display change. New ones go up before the old ones go, and the
    /// old ones stay until the new ones have drawn, because a new overlay shows nothing at first.
    /// </summary>
    public void Rebuild()
    {
        if (_content is null)
        {
            return;
        }

        List<OverlayWindow> replacements;
        try
        {
            replacements = CreateWindows(_content, fadesIn: false);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // The hooks still block input, so a stale layout beats dropping the lock over it.
            AppLog.Error("The lock overlay could not be rebuilt.", e);
            return;
        }

        _retiring.AddRange(_windows);
        _windows = replacements;
        replacements.ForEach(window => window.ContentRendered += OnReplacementRendered);

        // Rendering is not guaranteed, for example on a monitor that just went away.
        _retireFallback.Stop();
        _retireFallback.Start();

        _lastMinute = -1;
        UpdateClock();
        ApplyPinState();
        UpdateElevatedHint();
    }

    public void SetPhase(LockPhase phase, TimeSpan cooldown)
    {
        if (phase == LockPhase.Unlocked)
        {
            return;
        }

        _phase = phase;
        if (phase is LockPhase.Locked or LockPhase.CoolingDown)
        {
            _pinWasRejected = false;
        }

        ApplyPinState(cooldown);
    }

    public void SetPinProgress(int entered, int length)
    {
        _pinEntered = entered;
        _pinLength = length;
        if (entered > 0)
        {
            _pinWasRejected = false;
        }

        ApplyPinState();
    }

    public void RejectPin()
    {
        _pinWasRejected = true;
        _windows.ForEach(window => window.ShakePinPanel());
    }

    public void ShowInputAttempt()
    {
        if (_phase == LockPhase.Locked)
        {
            _windows.ForEach(window => window.ShowInputAttempt());
        }
    }

    /// <summary>
    /// Tries to move focus off an elevated window. The engine repeats this for as long as the window
    /// has focus, so a repeat means the last attempt did not work and the unlock key may be dead.
    /// </summary>
    public void TakeForeground()
    {
        // Touching only the primary overlay keeps this cheap, as the engine repeats it twice a second.
        var primary = _windows.FirstOrDefault();
        if (primary is null)
        {
            return;
        }

        if (!primary.TakeForeground() && !_loggedFocusRefusal)
        {
            _loggedFocusRefusal = true;
            AppLog.Warn("Windows refused to move focus to the lock screen, so the unlock key may not work.");
        }

        _elevatedFocus.OnReport(Environment.TickCount64);
        UpdateElevatedHint();
    }

    public void ReassertTopmost() => _windows.ForEach(window => window.ReassertTopmost());

    private static OverlayContent CreateContent(AppSettings settings) => new(
        settings.ScreenStyle,
        settings.ShowClock,
        settings.CustomMessage.Trim(),
        settings.ShowUnlockHint ? $"Press {KeyNames.GetDisplayName(settings.UnlockKey)} to unlock" : null);

    private static List<OverlayWindow> CreateWindows(OverlayContent content, bool fadesIn)
    {
        var windows = new List<OverlayWindow>();

        try
        {
            // The primary monitor comes first, so that it is the one that takes the foreground.
            foreach (var screen in System.Windows.Forms.Screen.AllScreens.OrderByDescending(screen => screen.Primary))
            {
                var bounds = screen.Bounds;
                var window = new OverlayWindow(
                    content,
                    new ScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    screen.Primary,
                    fadesIn);

                windows.Add(window);
                window.Show();
            }
        }
        catch
        {
            CloseAll(windows);
            throw;
        }

        return windows;
    }

    private static void CloseAll(List<OverlayWindow> windows) => windows.ForEach(window => window.CloseForGood());

    private void OnReplacementRendered(object? sender, EventArgs e)
    {
        if (_windows.TrueForAll(window => window.HasRendered))
        {
            CloseRetiring();
        }
    }

    private void CloseRetiring()
    {
        _retireFallback.Stop();
        CloseAll(_retiring);
        _retiring = [];
    }

    private void UpdateElevatedHint() =>
        _windows.FirstOrDefault()?.ShowElevatedHint(_elevatedFocus.ShouldShowHint(Environment.TickCount64));

    private void OnTick(object? sender, EventArgs e)
    {
        UpdateClock();
        UpdateElevatedHint();

        if (_phase == LockPhase.CoolingDown)
        {
            ApplyPinState();
        }

        if (++_tick % TopmostEverySeconds == 0)
        {
            ReassertTopmost();
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;

        foreach (var window in _windows)
        {
            window.ShowTime(now);
        }

        if (now.Minute != _lastMinute)
        {
            _lastMinute = now.Minute;
            _windows.ForEach(window => window.Drift());
        }
    }

    private void ApplyPinState(TimeSpan? cooldown = null)
    {
        switch (_phase)
        {
            case LockPhase.PinEntry:
                ShowPinPanel(EnterPinTitle, _pinWasRejected ? RejectedStatus : CancelHint, _pinEntered, _pinLength);
                break;

            case LockPhase.VerifyingPin:
                ShowPinPanel(EnterPinTitle, CheckingStatus, _pinEntered, _pinLength);
                break;

            case LockPhase.CoolingDown:
                // The countdown changes every second, so only its start is read out.
                ShowPinPanel(CooldownTitle, FormatCooldown(cooldown ?? _cooldownRemaining()), 0, -1, announce: cooldown is not null);
                break;

            default:
                _windows.ForEach(window => window.HidePinPanel());
                break;
        }
    }

    private void ShowPinPanel(string title, string status, int entered, int length, bool announce = true) =>
        _windows.ForEach(window => window.ShowPinPanel(title, status, entered, length, announce));

    private static string FormatCooldown(TimeSpan remaining)
    {
        // Rounded up, so that the countdown never shows 0:00 while the key is still ignored.
        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return string.Create(CultureInfo.CurrentCulture, $"Try again in {seconds / 60}:{seconds % 60:00}");
    }
}
