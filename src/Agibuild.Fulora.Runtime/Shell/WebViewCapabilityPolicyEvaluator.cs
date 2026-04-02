namespace Agibuild.Fulora.Shell;

/// <summary>
/// Maps host capability operations onto stable capability descriptors and effective policy decisions.
/// </summary>
public sealed class WebViewCapabilityPolicyEvaluator
{
    private const string HostBridgeSourceComponent = "host-bridge";

    private static readonly IReadOnlyDictionary<WebViewHostCapabilityOperation, WebViewCapabilityDescriptor> s_descriptors =
        new Dictionary<WebViewHostCapabilityOperation, WebViewCapabilityDescriptor>
        {
            [WebViewHostCapabilityOperation.ClipboardReadText] = new("clipboard.read", HostBridgeSourceComponent, WebViewHostCapabilityOperation.ClipboardReadText),
            [WebViewHostCapabilityOperation.ClipboardWriteText] = new("clipboard.write", HostBridgeSourceComponent, WebViewHostCapabilityOperation.ClipboardWriteText),
            [WebViewHostCapabilityOperation.FileDialogOpen] = new("filesystem.pick", HostBridgeSourceComponent, WebViewHostCapabilityOperation.FileDialogOpen),
            [WebViewHostCapabilityOperation.FileDialogSave] = new("filesystem.pick", HostBridgeSourceComponent, WebViewHostCapabilityOperation.FileDialogSave),
            [WebViewHostCapabilityOperation.ExternalOpen] = new("shell.external_open", HostBridgeSourceComponent, WebViewHostCapabilityOperation.ExternalOpen),
            [WebViewHostCapabilityOperation.NotificationShow] = new("notification.post", HostBridgeSourceComponent, WebViewHostCapabilityOperation.NotificationShow),
            [WebViewHostCapabilityOperation.MenuApplyModel] = new("window.chrome.modify", HostBridgeSourceComponent, WebViewHostCapabilityOperation.MenuApplyModel),
            [WebViewHostCapabilityOperation.TrayUpdateState] = new("window.chrome.modify", HostBridgeSourceComponent, WebViewHostCapabilityOperation.TrayUpdateState),
            [WebViewHostCapabilityOperation.SystemActionExecute] = new("shell.system_action.execute", HostBridgeSourceComponent, WebViewHostCapabilityOperation.SystemActionExecute),
            [WebViewHostCapabilityOperation.TrayInteractionEventDispatch] = new("shell.integration.event.dispatch", HostBridgeSourceComponent, WebViewHostCapabilityOperation.TrayInteractionEventDispatch),
            [WebViewHostCapabilityOperation.MenuInteractionEventDispatch] = new("shell.integration.event.dispatch", HostBridgeSourceComponent, WebViewHostCapabilityOperation.MenuInteractionEventDispatch),
            [WebViewHostCapabilityOperation.GlobalShortcutRegister] = new("shell.shortcut.register", HostBridgeSourceComponent, WebViewHostCapabilityOperation.GlobalShortcutRegister)
        };

    /// <summary>Returns the stable capability descriptor for the given operation.</summary>
    public static WebViewCapabilityDescriptor Describe(WebViewHostCapabilityOperation operation)
    {
        if (s_descriptors.TryGetValue(operation, out var descriptor))
            return descriptor;

        throw new ArgumentOutOfRangeException(nameof(operation), operation, "No capability descriptor is registered for the requested host capability operation.");
    }

    /// <summary>Returns the stable capability descriptor for the given context.</summary>
    public WebViewCapabilityDescriptor Describe(in WebViewHostCapabilityRequestContext context)
        => Describe(context.Operation);

    /// <summary>Evaluates the effective policy decision for the given host capability context.</summary>
    public WebViewCapabilityPolicyDecision Evaluate(
        IWebViewHostCapabilityPolicy? policy,
        in WebViewHostCapabilityRequestContext context)
    {
        if (policy is null)
            return WebViewCapabilityPolicyDecision.Allow();

        var legacyDecision = policy.Evaluate(context);
        return legacyDecision.Kind switch
        {
            WebViewHostCapabilityDecisionKind.Allow => WebViewCapabilityPolicyDecision.Allow(),
            WebViewHostCapabilityDecisionKind.Deny => WebViewCapabilityPolicyDecision.Deny(legacyDecision.Reason),
            _ => throw new ArgumentOutOfRangeException(nameof(legacyDecision), legacyDecision.Kind, "Unsupported host capability decision kind.")
        };
    }
}
