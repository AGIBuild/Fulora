using System;

namespace Agibuild.Fulora.Shell;

/// <summary>
/// Thin runtime façade for download and permission request governance.
/// </summary>
internal sealed class ShellRequestGovernanceRuntime
{
    private readonly IWebView _webView;
    private readonly WebViewShellExperienceOptions _options;
    private readonly Guid _rootWindowId;
    private readonly WebViewHostCapabilityExecutor _executor;
    private readonly WebViewShellSessionDecision? _sessionDecision;
    private readonly WebViewSessionPermissionProfile? _rootProfile;
    private readonly Action<Guid, Guid?, string, WebViewSessionPermissionProfile, WebViewShellSessionDecision, WebViewPermissionKind?, WebViewPermissionProfileDecision> _raiseSessionPermissionProfileDiagnostic;

    public ShellRequestGovernanceRuntime(
        IWebView webView,
        WebViewShellExperienceOptions options,
        Guid rootWindowId,
        WebViewHostCapabilityExecutor executor,
        WebViewShellSessionDecision? sessionDecision,
        WebViewSessionPermissionProfile? rootProfile,
        Action<Guid, Guid?, string, WebViewSessionPermissionProfile, WebViewShellSessionDecision, WebViewPermissionKind?, WebViewPermissionProfileDecision> raiseSessionPermissionProfileDiagnostic)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rootWindowId = rootWindowId;
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _sessionDecision = sessionDecision;
        _rootProfile = rootProfile;
        _raiseSessionPermissionProfileDiagnostic = raiseSessionPermissionProfileDiagnostic
                                                  ?? throw new ArgumentNullException(nameof(raiseSessionPermissionProfileDiagnostic));
    }

    public void HandleDownloadRequested(DownloadRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        _executor.ExecutePolicyDomain(
            WebViewShellPolicyDomain.Download,
            () => _options.DownloadPolicy?.Handle(_webView, args));
        _executor.ExecutePolicyDomain(
            WebViewShellPolicyDomain.Download,
            () => _options.DownloadHandler?.Invoke(_webView, args));
    }

    public void HandlePermissionRequested(PermissionRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var appliedProfileDecision = TryApplyProfilePermissionDecision(args);
        if (appliedProfileDecision)
            return;

        _executor.ExecutePolicyDomain(
            WebViewShellPolicyDomain.Permission,
            () => _options.PermissionPolicy?.Handle(_webView, args));
        _executor.ExecutePolicyDomain(
            WebViewShellPolicyDomain.Permission,
            () => _options.PermissionHandler?.Invoke(_webView, args));
    }

    private bool TryApplyProfilePermissionDecision(PermissionRequestedEventArgs args)
    {
        if (_options.SessionPermissionProfileResolver is null)
            return false;

        var scopeIdentity = _sessionDecision?.ScopeIdentity ?? _options.SessionContext.ScopeIdentity;
        var profileContext = new WebViewSessionPermissionProfileContext(
            _rootWindowId,
            ParentWindowId: null,
            WindowId: _rootWindowId,
            ScopeIdentity: scopeIdentity,
            RequestUri: args.Origin,
            PermissionKind: args.PermissionKind);

        var resolvedProfile = _executor.ExecutePolicyDomain(
            WebViewShellPolicyDomain.Permission,
            () => _options.SessionPermissionProfileResolver.Resolve(profileContext, _rootProfile));

        if (resolvedProfile is null)
            return false;

        var effectiveSessionDecision = resolvedProfile.ResolveSessionDecision(
            parentDecision: null,
            fallbackDecision: _sessionDecision,
            scopeIdentity: scopeIdentity);
        var profileDecision = resolvedProfile.ResolvePermissionDecision(args.PermissionKind);

        _raiseSessionPermissionProfileDiagnostic(
            _rootWindowId,
            null,
            scopeIdentity,
            resolvedProfile,
            effectiveSessionDecision,
            args.PermissionKind,
            profileDecision);

        if (!profileDecision.IsExplicit || profileDecision.State == PermissionState.Default)
            return false;

        args.State = profileDecision.State;
        return true;
    }
}
