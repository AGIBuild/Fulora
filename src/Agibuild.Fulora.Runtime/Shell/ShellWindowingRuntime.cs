using System;

namespace Agibuild.Fulora.Shell;

/// <summary>
/// Thin runtime façade for shell windowing and policy-evaluation collaborators.
/// Keeps policy execution and host bridge access out of higher-level coordinators.
/// </summary>
internal sealed class ShellWindowingRuntime
{
    private const string HostCapabilityBridgeUnavailableReason = "host-capability-bridge-not-configured";

    private readonly IWebView _webView;
    private readonly WebViewShellExperienceOptions _options;
    private readonly Guid _rootWindowId;
    private readonly WebViewHostCapabilityExecutor _executor;

    public ShellWindowingRuntime(
        IWebView webView,
        WebViewShellExperienceOptions options,
        Guid rootWindowId,
        WebViewHostCapabilityExecutor executor)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rootWindowId = rootWindowId;
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public WebViewShellSessionDecision? ResolveSessionDecision(
        WebViewShellSessionContext context,
        WebViewShellSessionDecision? fallbackDecision)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _options.SessionPolicy is null
            ? fallbackDecision
            : _executor.ExecutePolicyDomain(
                WebViewShellPolicyDomain.Session,
                () => _options.SessionPolicy.Resolve(context));
    }

    public WebViewSessionPermissionProfile? ResolveSessionPermissionProfile(
        WebViewSessionPermissionProfileContext context,
        WebViewSessionPermissionProfile? parentProfile)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _options.SessionPermissionProfileResolver is null
            ? null
            : _executor.ExecutePolicyDomain(
                WebViewShellPolicyDomain.Session,
                () => _options.SessionPermissionProfileResolver.Resolve(context, parentProfile));
    }

    public IWebView? CreateManagedWindow(WebViewManagedWindowCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _executor.ExecutePolicyDomain(
            WebViewShellPolicyDomain.ManagedWindowLifecycle,
            () => _options.ManagedWindowFactory?.Invoke(context));
    }

    public WebViewNewWindowStrategyDecision ResolveNewWindowStrategy(
        NewWindowRequestedEventArgs args,
        Guid candidateWindowId)
    {
        ArgumentNullException.ThrowIfNull(args);

        var policyContext = new WebViewNewWindowPolicyContext(
            SourceWindowId: _rootWindowId,
            CandidateWindowId: candidateWindowId,
            TargetUri: args.Uri,
            ScopeIdentity: _options.SessionContext.ScopeIdentity);

        return _executor.ExecutePolicyDomain(
                   WebViewShellPolicyDomain.NewWindow,
                   () => _options.NewWindowPolicy?.Decide(_webView, args, policyContext)
                         ?? WebViewNewWindowStrategyDecision.InPlace())
               ?? WebViewNewWindowStrategyDecision.InPlace();
    }

    public WebViewHostCapabilityCallResult<object?> OpenExternal(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (_options.HostCapabilityBridge is null)
        {
            var capability = WebViewCapabilityPolicyEvaluator.Describe(WebViewHostCapabilityOperation.ExternalOpen);
            var decision = WebViewCapabilityPolicyDecision.Deny(HostCapabilityBridgeUnavailableReason);
            return WebViewHostCapabilityCallResult<object?>.Denied(capability, decision, HostCapabilityBridgeUnavailableReason);
        }

        return _options.HostCapabilityBridge.OpenExternal(
            uri,
            _rootWindowId,
            parentWindowId: _rootWindowId,
            targetWindowId: null);
    }
}
