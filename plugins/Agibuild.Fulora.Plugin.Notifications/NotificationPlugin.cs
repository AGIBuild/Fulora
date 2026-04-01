using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.Notifications;

/// <summary>
/// Bridge plugin manifest for the Notifications service.
/// Register with: <c>bridge.UsePlugin&lt;NotificationPlugin&gt;();</c>
/// Uses <see cref="InMemoryNotificationProvider"/> by default for testing; replace via DI for production.
/// </summary>
public sealed class NotificationPlugin : IBridgePlugin
{
    /// <summary>Returns policy metadata for the Notifications plugin.</summary>
    public static BridgePluginMetadata GetMetadata()
        => new(
            "Agibuild.Fulora.Plugin.Notifications",
            ["plugin.notification.post"],
            [],
            [
                "Delivery guarantees and presentation differ by platform and user settings.",
                "Production hosts should replace the in-memory provider and respect user consent."
            ],
            ["desktop-hosts", "platform-delivery-variance"]);

    /// <summary>Returns the service descriptors for the Notifications plugin.</summary>
    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<INotificationService>(
            _ => new NotificationService(new InMemoryNotificationProvider()));
    }
}
