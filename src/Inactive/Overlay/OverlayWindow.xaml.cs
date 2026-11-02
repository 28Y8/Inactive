using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Inactive.Controls;
using Inactive.Core.Settings;
using Inactive.Native;

namespace Inactive.Overlay;

/// <summary>What every overlay shows for the current lock.</summary>
internal sealed record OverlayContent(ScreenStyle Style, bool ShowClock, string Message, string? Hint);

/// <summary>A monitor rectangle in physical pixels.</summary>
internal readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

/// <summary>The borderless window that covers one monitor while locked.</summary>
internal sealed partial class OverlayWindow : Window
{
    private const double DimmedOpacity = 0.7;
    private const int MaxDriftPixels = 8;
    private const double DotSize = 14;
    private const double DotGap = 12;

    private static readonly Color CoveredColor = Color.FromRgb(5, 5, 5);
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(200);

    private readonly ScreenBounds _bounds;
    private readonly bool _isPrimary;
    private readonly bool _showsClock;
    private readonly bool _fadesIn;
    private readonly double _shadeOpacity;
    private readonly Brush _dotFilled = Brushes.White;
    private readonly Brush _dotEmpty;
    private nint _handle;
    private bool _hasClockBlock;
    private bool _canClose;

    /// <param name="content">What to show.</param>
    /// <param name="bounds">The monitor to cover.</param>
    /// <param name="isPrimary">True for the monitor that shows the clock.</param>
    /// <param name="fadesIn">False when replacing a window, so that the screen never shows through.</param>
    public OverlayWindow(OverlayContent content, ScreenBounds bounds, bool isPrimary, bool fadesIn)
    {
        InitializeComponent();

        _bounds = bounds;
        _isPrimary = isPrimary;
        _fadesIn = fadesIn;
        _dotEmpty = (Brush)FindResource("DotEmptyBrush");
        _showsClock = isPrimary && content.ShowClock && content.Style != ScreenStyle.Visible;

        _shadeOpacity = content.Style switch
        {
            ScreenStyle.Dimmed => DimmedOpacity,
            ScreenStyle.Covered => 1,
            _ => 0,
        };

        Shade.Background = new SolidColorBrush(content.Style == ScreenStyle.Covered ? CoveredColor : Colors.Black);
        Shade.Opacity = fadesIn ? 0 : _shadeOpacity;

        if (content.Style != ScreenStyle.Visible)
        {
            Cursor = Cursors.None;
        }

        ApplyContent(content);
        Loaded += OnLoaded;
    }

    /// <summary>True once the window has drawn its first frame.</summary>
    public bool HasRendered { get; private set; }

    /// <summary>Lets the window close. Until then it refuses, so nothing else can take it away.</summary>
    public void CloseForGood()
    {
        _canClose = true;
        Close();
    }

    public void ReassertTopmost()
    {
        if (_handle != 0)
        {
            NativeMethods.SetWindowPos(
                _handle,
                NativeMethods.HwndTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }
    }

    /// <summary>
    /// Best effort: hooks go blind while an elevated window has focus, so the focus has to come
    /// here. Windows may refuse, which is why the lock never relies on this.
    /// </summary>
    /// <returns>False when Windows refused.</returns>
    public bool TakeForeground() => _handle != 0 && NativeMethods.SetForegroundWindow(_handle);

    /// <summary>Shows or hides the way out for when the unlock key may not be getting through.</summary>
    public void ShowElevatedHint(bool isShown) =>
        ElevatedHintPill.Visibility = isShown ? Visibility.Visible : Visibility.Collapsed;

    public void ShowTime(DateTime now)
    {
        if (_showsClock)
        {
            TimeText.Text = now.ToString("t", CultureInfo.CurrentCulture);
            DateText.Text = now.ToString("D", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>Moves the clock a little, so that it never burns into the panel.</summary>
    public void Drift()
    {
        ClockDrift.X = Random.Shared.Next(-MaxDriftPixels, MaxDriftPixels + 1);
        ClockDrift.Y = Random.Shared.Next(-MaxDriftPixels, MaxDriftPixels + 1);
    }

    public void ShowInputAttempt()
    {
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2.3) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(150)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(2)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(2.3)));
        AttemptPill.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Shows the PIN panel. A negative <paramref name="length"/> hides the dots.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="status">The line below the dots.</param>
    /// <param name="entered">How many digits are in.</param>
    /// <param name="length">How many digits the PIN has.</param>
    /// <param name="announce">Read a changed status out to screen readers.</param>
    public void ShowPinPanel(string title, string status, int entered, int length, bool announce)
    {
        var statusChanged = PinStatus.Text != status;

        PinTitle.Text = title;
        PinStatus.Text = status;
        SetDots(entered, length);

        PinPanel.Visibility = Visibility.Visible;
        ClockBlock.Visibility = Visibility.Collapsed;

        // One voice is enough with several monitors.
        if (announce && statusChanged && _isPrimary)
        {
            LiveRegion.Announce(PinStatus);
        }
    }

    public void HidePinPanel()
    {
        PinPanel.Visibility = Visibility.Collapsed;
        ClockBlock.Visibility = _hasClockBlock ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShakePinPanel()
    {
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(400) };
        double[] offsets = [-12, 12, -8, 8, -4, 4, 0];

        for (var i = 0; i < offsets.Length; i++)
        {
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(offsets[i], TimeSpan.FromMilliseconds(400.0 * (i + 1) / offsets.Length)));
        }

        PinShake.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;

        var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLongPtr(
            _handle,
            NativeMethods.GwlExStyle,
            (nint)((long)style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));

        ApplyBounds();
    }

    // Moving onto a monitor with another scale makes WPF resize the window by the scale ratio.
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyBounds();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        HasRendered = true;
        base.OnContentRendered(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_canClose;
        base.OnClosing(e);
    }

    private void ApplyContent(OverlayContent content)
    {
        var hasMessage = content.Message.Length > 0;
        var hasHint = content.Hint is not null;

        if (content.Style == ScreenStyle.Visible)
        {
            LockedPill.Visibility = Visibility.Visible;
            LockedPillText.Text = _isPrimary ? ComposePillText(content) : "Locked";
            return;
        }

        if (!_isPrimary)
        {
            return;
        }

        TimeText.Visibility = _showsClock ? Visibility.Visible : Visibility.Collapsed;
        DateText.Visibility = _showsClock ? Visibility.Visible : Visibility.Collapsed;

        MessageText.Text = content.Message;
        MessageText.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;

        HintText.Text = content.Hint;
        HintText.Visibility = hasHint ? Visibility.Visible : Visibility.Collapsed;

        _hasClockBlock = _showsClock || hasMessage || hasHint;
        ClockBlock.Visibility = _hasClockBlock ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string ComposePillText(OverlayContent content)
    {
        string?[] parts = ["Locked", content.Message.Length > 0 ? content.Message : null, content.Hint];
        return string.Join(". ", parts.OfType<string>());
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // WPF settles the size after SourceInitialized, so the placement is repeated once here.
        ApplyBounds();
        ReassertTopmost();

        if (_fadesIn)
        {
            Shade.BeginAnimation(OpacityProperty, new DoubleAnimation(0, _shadeOpacity, FadeIn));
        }
    }

    private void ApplyBounds() =>
        NativeMethods.SetWindowPos(
            _handle,
            NativeMethods.HwndTopmost,
            _bounds.X,
            _bounds.Y,
            _bounds.Width,
            _bounds.Height,
            NativeMethods.SwpNoActivate);

    private void SetDots(int entered, int length)
    {
        PinDots.Visibility = length < 0 ? Visibility.Collapsed : Visibility.Visible;

        if (PinDots.Children.Count != Math.Max(length, 0))
        {
            PinDots.Children.Clear();
            for (var i = 0; i < length; i++)
            {
                PinDots.Children.Add(new Ellipse
                {
                    Width = DotSize,
                    Height = DotSize,
                    Margin = new Thickness(DotGap / 2, 0, DotGap / 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
        }

        for (var i = 0; i < PinDots.Children.Count; i++)
        {
            ((Ellipse)PinDots.Children[i]).Fill = i < entered ? _dotFilled : _dotEmpty;
        }
    }
}
