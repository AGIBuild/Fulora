using System;
using Agibuild.Fulora;

namespace Agibuild.Fulora.Shell;

internal sealed class WebViewNewWindowHandler
{
    private const string HostCapabilityBridgeUnavailableReason = "host-capability-bridge-not-configured";

    private readonly WebViewManagedWindowManager _managedWindowManager;
    private readonly ShellWindowingRuntime _windowingRuntime;
    private readonly Action<WebViewShellPolicyDomain, Exception> _reportPolicyFailure;

    public WebViewNewWindowHandler(
        IWebView webView,
        WebViewShellExperienceOptions options,
        WebViewManagedWindowManager managedWindowManager,
        ShellWindowingRuntime windowingRuntime,
        Action<WebViewShellPolicyDomain, Exception> reportPolicyFailure)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(options);
        _managedWindowManager = managedWindowManager ?? throw new ArgumentNullException(nameof(managedWindowManager));
        _windowingRuntime = windowingRuntime ?? throw new ArgumentNullException(nameof(windowingRuntime));
        _reportPolicyFailure = reportPolicyFailure ?? throw new ArgumentNullException(nameof(reportPolicyFailure));
    }

    public void Handle(NewWindowRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var candidateWindowId = Guid.NewGuid();
        var decision = ResolveNewWindowStrategy(candidateWindowId, args)
                       ?? WebViewNewWindowStrategyDecision.InPlace();
        ExecuteStrategyDecision(decision, candidateWindowId, args);
    }

    private WebViewNewWindowStrategyDecision? ResolveNewWindowStrategy(Guid candidateWindowId, NewWindowRequestedEventArgs args)
        => _windowingRuntime.ResolveNewWindowStrategy(args, candidateWindowId);

    private void ExecuteStrategyDecision(WebViewNewWindowStrategyDecision decision, Guid candidateWindowId, NewWindowRequestedEventArgs args)
    {
        switch (decision.Strategy)
        {
            case WebViewNewWindowStrategy.InPlace:
                args.Handled = false;
                return;
            case WebViewNewWindowStrategy.ManagedWindow:
                HandleManagedWindowStrategy(decision, candidateWindowId, args);
                return;
            case WebViewNewWindowStrategy.ExternalBrowser:
                HandleExternalBrowserStrategy(args);
                return;
            case WebViewNewWindowStrategy.Delegate:
                args.Handled = decision.Handled;
                return;
            default:
                args.Handled = false;
                return;
        }
    }

    private void HandleManagedWindowStrategy(
        WebViewNewWindowStrategyDecision decision,
        Guid candidateWindowId,
        NewWindowRequestedEventArgs args)
    {
        var created = _managedWindowManager.TryCreateManagedWindow(candidateWindowId, args.Uri, decision.ScopeIdentityOverride);
        args.Handled = created;
        if (!created)
            args.Handled = false;
    }

    private void HandleExternalBrowserStrategy(NewWindowRequestedEventArgs args)
    {
        if (args.Uri is null)
        {
            args.Handled = true;
            _reportPolicyFailure(
                WebViewShellPolicyDomain.ExternalOpen,
                new InvalidOperationException("External open strategy requires a non-null target URI."));
            return;
        }

        var openResult = _windowingRuntime.OpenExternal(args.Uri);

        args.Handled = true;
        if (openResult.Outcome == WebViewHostCapabilityCallOutcome.Deny)
        {
            if (string.Equals(openResult.DenyReason, HostCapabilityBridgeUnavailableReason, StringComparison.Ordinal))
            {
                _reportPolicyFailure(
                    WebViewShellPolicyDomain.ExternalOpen,
                    new InvalidOperationException("Host capability bridge is required for ExternalBrowser strategy."));
                return;
            }

            _reportPolicyFailure(
                WebViewShellPolicyDomain.ExternalOpen,
                new UnauthorizedAccessException(openResult.DenyReason ?? "External open was denied by host capability policy."));
            return;
        }

        if (openResult.Outcome == WebViewHostCapabilityCallOutcome.Failure)
        {
            _reportPolicyFailure(
                WebViewShellPolicyDomain.ExternalOpen,
                openResult.Error ?? new InvalidOperationException("External open failed without an exception payload."));
        }
    }
}
