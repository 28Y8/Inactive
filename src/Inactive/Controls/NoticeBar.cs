using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Inactive.Controls;

public enum NoticeSeverity
{
    Information,
    Success,
    Caution,
    Critical,
}

/// <summary>An inline message in the style of a WinUI InfoBar. Its content is an optional action.</summary>
public sealed class NoticeBar : ContentControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(NoticeBar));

    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(NoticeSeverity), typeof(NoticeBar), new PropertyMetadata(NoticeSeverity.Information));

    static NoticeBar()
    {
        FocusableProperty.OverrideMetadata(typeof(NoticeBar), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(NoticeBar), new FrameworkPropertyMetadata(false));
    }

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public NoticeSeverity Severity
    {
        get => (NoticeSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>Shows the bar with a message, or hides it when the message is null.</summary>
    public void Show(string? message, NoticeSeverity? severity = null)
    {
        // Pages show their notices again on every refresh, and that is not news.
        var isNews = message is not null && (message != Message || Visibility != Visibility.Visible);

        Message = message;
        if (severity is { } value)
        {
            Severity = value;
        }

        Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;

        if (isNews)
        {
            LiveRegion.Announce(this);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new NoticeBarAutomationPeer(this);

    // A plain ContentControl has no peer, and the text lives in the template, not in the content.
    private sealed class NoticeBarAutomationPeer(NoticeBar owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(NoticeBar);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.StatusBar;

        protected override string GetNameCore() => ((NoticeBar)Owner).Message ?? string.Empty;
    }
}
