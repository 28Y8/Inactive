using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

namespace Inactive.Controls;

/// <summary>
/// Reads changed text out to screen readers. AutomationProperties.LiveSetting only marks an element
/// as a live region: WPF never raises the event by itself, so whoever changes the text has to.
/// </summary>
internal static class LiveRegion
{
    /// <summary>
    /// Announces an element after its text or visibility changed. This waits for layout, because an
    /// element that has only just become visible is not announced yet.
    /// </summary>
    public static void Announce(UIElement element) =>
        element.Dispatcher.BeginInvoke(() => Raise(element), DispatcherPriority.Loaded);

    private static void Raise(UIElement element)
    {
        var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
