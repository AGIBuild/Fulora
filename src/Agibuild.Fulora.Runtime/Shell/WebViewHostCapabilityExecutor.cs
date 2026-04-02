using System;

namespace Agibuild.Fulora.Shell;

internal sealed class WebViewHostCapabilityExecutor
{
    private const string HostCapabilityBridgeUnavailableReason = "host-capability-bridge-not-configured";

    private readonly IWebView _webView;
    private readonly WebViewShellExperienceOptions _options;
    private readonly Guid _rootWindowId;
    private readonly Func<WebViewMenuModelRequest, WebViewMenuModelRequest> _normalizeMenuModel;
    private readonly Func<WebViewMenuModelRequest, (WebViewMenuModelRequest? EffectiveMenuModel, WebViewHostCapabilityCallResult<object?>? Result)> _tryPruneMenuModel;
    private readonly Action<WebViewMenuModelRequest> _updateEffectiveMenuModel;
    private readonly Func<WebViewSystemAction, bool> _isSystemActionWhitelisted;
    private readonly Action<WebViewShellPolicyDomain, Exception> _reportPolicyFailure;

    public WebViewHostCapabilityExecutor(
        IWebView webView,
        WebViewShellExperienceOptions options,
        Guid rootWindowId,
        Func<WebViewMenuModelRequest, WebViewMenuModelRequest> normalizeMenuModel,
        Func<WebViewMenuModelRequest, (WebViewMenuModelRequest? EffectiveMenuModel, WebViewHostCapabilityCallResult<object?>? Result)> tryPruneMenuModel,
        Action<WebViewMenuModelRequest> updateEffectiveMenuModel,
        Func<WebViewSystemAction, bool> isSystemActionWhitelisted,
        Action<WebViewShellPolicyDomain, Exception> reportPolicyFailure)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rootWindowId = rootWindowId;
        _normalizeMenuModel = normalizeMenuModel ?? throw new ArgumentNullException(nameof(normalizeMenuModel));
        _tryPruneMenuModel = tryPruneMenuModel ?? throw new ArgumentNullException(nameof(tryPruneMenuModel));
        _updateEffectiveMenuModel = updateEffectiveMenuModel ?? throw new ArgumentNullException(nameof(updateEffectiveMenuModel));
        _isSystemActionWhitelisted = isSystemActionWhitelisted ?? throw new ArgumentNullException(nameof(isSystemActionWhitelisted));
        _reportPolicyFailure = reportPolicyFailure ?? throw new ArgumentNullException(nameof(reportPolicyFailure));
    }

    public WebViewHostCapabilityCallResult<string?> ReadClipboardText()
    {
        if (_options.HostCapabilityBridge is null)
            return CreateUnavailableResult<string?>(WebViewHostCapabilityOperation.ClipboardReadText);
        return _options.HostCapabilityBridge.ReadClipboardText(_rootWindowId, parentWindowId: null, targetWindowId: _rootWindowId);
    }

    public WebViewHostCapabilityCallResult<object?> WriteClipboardText(string text)
    {
        if (_options.HostCapabilityBridge is null)
            return CreateUnavailableResult<object?>(WebViewHostCapabilityOperation.ClipboardWriteText);
        return _options.HostCapabilityBridge.WriteClipboardText(text, _rootWindowId, parentWindowId: null, targetWindowId: _rootWindowId);
    }

    public WebViewHostCapabilityCallResult<WebViewFileDialogResult> ShowOpenFileDialog(WebViewOpenFileDialogRequest request)
    {
        if (_options.HostCapabilityBridge is null)
            return CreateUnavailableResult<WebViewFileDialogResult>(WebViewHostCapabilityOperation.FileDialogOpen);
        return _options.HostCapabilityBridge.ShowOpenFileDialog(request, _rootWindowId, parentWindowId: null, targetWindowId: _rootWindowId);
    }

    public WebViewHostCapabilityCallResult<WebViewFileDialogResult> ShowSaveFileDialog(WebViewSaveFileDialogRequest request)
    {
        if (_options.HostCapabilityBridge is null)
            return CreateUnavailableResult<WebViewFileDialogResult>(WebViewHostCapabilityOperation.FileDialogSave);
        return _options.HostCapabilityBridge.ShowSaveFileDialog(request, _rootWindowId, parentWindowId: null, targetWindowId: _rootWindowId);
    }

    public WebViewHostCapabilityCallResult<object?> ShowNotification(WebViewNotificationRequest request)
    {
        if (_options.HostCapabilityBridge is null)
            return CreateUnavailableResult<object?>(WebViewHostCapabilityOperation.NotificationShow);
        return _options.HostCapabilityBridge.ShowNotification(request, _rootWindowId, parentWindowId: null, targetWindowId: _rootWindowId);
    }

    public WebViewHostCapabilityCallResult<object?> ApplyMenuModel(WebViewMenuModelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = _normalizeMenuModel(request);
        var prunedResult = _tryPruneMenuModel(normalizedRequest);
        if (prunedResult.Result is not null)
            return prunedResult.Result;
        var effectiveMenuModel = prunedResult.EffectiveMenuModel ?? normalizedRequest;

        if (_options.HostCapabilityBridge is null)
        {
            var unavailable = CreateUnavailableResult<object?>(WebViewHostCapabilityOperation.MenuApplyModel);
            ReportSystemIntegrationOutcome(
                unavailable,
                "Menu model operation was denied by host capability policy.");
            return unavailable;
        }

        var result = _options.HostCapabilityBridge.ApplyMenuModel(
            effectiveMenuModel,
            _rootWindowId,
            parentWindowId: null,
            targetWindowId: _rootWindowId);
        if (result.Outcome == WebViewHostCapabilityCallOutcome.Allow)
            _updateEffectiveMenuModel(effectiveMenuModel);
        ReportSystemIntegrationOutcome(
            result,
            "Menu model operation was denied by host capability policy.");
        return result;
    }

    public WebViewHostCapabilityCallResult<object?> UpdateTrayState(WebViewTrayStateRequest request)
    {
        if (_options.HostCapabilityBridge is null)
        {
            var unavailable = CreateUnavailableResult<object?>(WebViewHostCapabilityOperation.TrayUpdateState);
            ReportSystemIntegrationOutcome(
                unavailable,
                "Tray state operation was denied by host capability policy.");
            return unavailable;
        }

        var result = _options.HostCapabilityBridge.UpdateTrayState(
            request,
            _rootWindowId,
            parentWindowId: null,
            targetWindowId: _rootWindowId);
        ReportSystemIntegrationOutcome(
            result,
            "Tray state operation was denied by host capability policy.");
        return result;
    }

    public WebViewHostCapabilityCallResult<object?> ExecuteSystemAction(WebViewSystemActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_isSystemActionWhitelisted(request.Action))
        {
            var denied = CreateDeniedResult<object?>(WebViewHostCapabilityOperation.SystemActionExecute, "system-action-not-whitelisted");
            ReportSystemIntegrationOutcome(
                denied,
                "System action is not in shell whitelist.");
            return denied;
        }

        if (_options.HostCapabilityBridge is null)
        {
            var unavailable = CreateUnavailableResult<object?>(WebViewHostCapabilityOperation.SystemActionExecute);
            ReportSystemIntegrationOutcome(
                unavailable,
                "System action operation was denied by host capability policy.");
            return unavailable;
        }

        var result = _options.HostCapabilityBridge.ExecuteSystemAction(
            request,
            _rootWindowId,
            parentWindowId: null,
            targetWindowId: _rootWindowId);
        ReportSystemIntegrationOutcome(
            result,
            "System action operation was denied by host capability policy.");
        return result;
    }

    public WebViewHostCapabilityCallResult<WebViewSystemIntegrationEventRequest> PublishSystemIntegrationEvent(
        WebViewSystemIntegrationEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_options.HostCapabilityBridge is null)
        {
            var unavailable = CreateUnavailableResult<WebViewSystemIntegrationEventRequest>(
                request.Kind == WebViewSystemIntegrationEventKind.TrayInteracted
                    ? WebViewHostCapabilityOperation.TrayInteractionEventDispatch
                    : WebViewHostCapabilityOperation.MenuInteractionEventDispatch);
            ReportSystemIntegrationOutcome(
                unavailable,
                "System integration event dispatch was denied by host capability policy.");
            return unavailable;
        }

        var result = _options.HostCapabilityBridge.DispatchSystemIntegrationEvent(
            request,
            _rootWindowId,
            parentWindowId: null,
            targetWindowId: _rootWindowId);
        ReportSystemIntegrationOutcome(
            result,
            "System integration event dispatch was denied by host capability policy.");
        return result;
    }

    public void ExecutePolicyDomain(WebViewShellPolicyDomain domain, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
        }
        catch (Exception ex)
        {
            _reportPolicyFailure(domain, ex);
        }
    }

    public T? ExecutePolicyDomain<T>(WebViewShellPolicyDomain domain, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return action();
        }
        catch (Exception ex)
        {
            _reportPolicyFailure(domain, ex);
            return default;
        }
    }

    public void ReportSystemIntegrationOutcome<T>(
        WebViewHostCapabilityCallResult<T> result,
        string defaultDenyReason)
    {
        if (result.Outcome == WebViewHostCapabilityCallOutcome.Deny)
        {
            _reportPolicyFailure(
                WebViewShellPolicyDomain.SystemIntegration,
                new UnauthorizedAccessException(result.DenyReason ?? defaultDenyReason));
            return;
        }

        if (result.Outcome == WebViewHostCapabilityCallOutcome.Failure)
        {
            _reportPolicyFailure(
                WebViewShellPolicyDomain.SystemIntegration,
                result.Error ?? new InvalidOperationException("System integration capability failed without an exception payload."));
        }
    }

    private static WebViewHostCapabilityCallResult<T> CreateDeniedResult<T>(
        WebViewHostCapabilityOperation operation,
        string denyReason)
    {
        var capability = WebViewCapabilityPolicyEvaluator.Describe(operation);
        var policyDecision = WebViewCapabilityPolicyDecision.Deny(denyReason);
        return WebViewHostCapabilityCallResult<T>.Denied(capability, policyDecision, denyReason);
    }

    private static WebViewHostCapabilityCallResult<T> CreateUnavailableResult<T>(WebViewHostCapabilityOperation operation)
        => CreateDeniedResult<T>(operation, HostCapabilityBridgeUnavailableReason);
}
