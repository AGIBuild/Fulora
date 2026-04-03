using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Agibuild.Fulora.Shell;


/// <summary>
/// Options for host capability bridge boundary validation.
/// </summary>
public sealed class WebViewHostCapabilityBridgeOptions
{
    /// <summary>Minimum supported aggregate metadata budget.</summary>
    public const int MinSystemIntegrationMetadataTotalLength = 256;
    /// <summary>Maximum supported aggregate metadata budget.</summary>
    public const int MaxSystemIntegrationMetadataTotalLength = 4096;
    /// <summary>Default aggregate metadata budget.</summary>
    public const int DefaultSystemIntegrationMetadataTotalLength = 1024;

    /// <summary>
    /// Aggregate metadata budget for inbound system-integration events.
    /// Must be within <see cref="MinSystemIntegrationMetadataTotalLength"/> and
    /// <see cref="MaxSystemIntegrationMetadataTotalLength"/>.
    /// </summary>
    public int SystemIntegrationMetadataTotalLength { get; init; } = DefaultSystemIntegrationMetadataTotalLength;
}

/// <summary>
/// Runtime host capability bridge with policy-first deterministic execution semantics.
/// </summary>
public sealed class WebViewHostCapabilityBridge
{
    private const int MaxSystemIntegrationMetadataEntries = 8;
    private const int MaxSystemIntegrationMetadataKeyLength = 64;
    private const int MaxSystemIntegrationMetadataValueLength = 256;
    private const string SystemIntegrationMetadataAllowedPrefix = "platform.";
    private const string SystemIntegrationMetadataExtensionPrefix = "platform.extension.";
    private const string SystemIntegrationCoreFieldMissing = "system-integration-event-core-field-missing";
    private const string SystemIntegrationMetadataEnvelopeInvalid = "system-integration-event-metadata-envelope-invalid";
    private const string SystemIntegrationMetadataNamespaceInvalid = "system-integration-event-metadata-namespace-invalid";
    private const string SystemIntegrationMetadataUnregisteredKey = "system-integration-event-metadata-key-unregistered";
    private const string SystemIntegrationMetadataBudgetExceeded = "system-integration-event-metadata-budget-exceeded";
    private static readonly HashSet<string> ReservedSystemIntegrationMetadataKeys = new(StringComparer.Ordinal)
    {
        "platform.source",
        "platform.visibility",
        "platform.tooltipPresent",
        "platform.profileIdentity",
        "platform.profileVersion",
        "platform.profileHash",
        "platform.profilePermissionState",
        "platform.pruningStage"
    };

    private readonly IWebViewHostCapabilityProvider _provider;
    private readonly IWebViewHostCapabilityPolicy? _policy;
    private readonly IWebViewHostCapabilityPolicyV2? _policyV2;
    private readonly WebViewCapabilityPolicyEvaluator _policyEvaluator;
    private readonly int _maxSystemIntegrationMetadataTotalLength;

    /// <summary>
    /// Raised when a typed capability call is completed with deterministic outcome metadata.
    /// </summary>
    public event EventHandler<WebViewHostCapabilityDiagnosticEventArgs>? CapabilityCallCompleted;
    /// <summary>
    /// Raised when a typed inbound system integration event is allowed and dispatched.
    /// </summary>
    public event EventHandler<WebViewSystemIntegrationEventRequest>? SystemIntegrationEventDispatched;

    /// <summary>Create bridge with provider, optional authorization policy and boundary options.</summary>
    public WebViewHostCapabilityBridge(
        IWebViewHostCapabilityProvider provider,
        IWebViewHostCapabilityPolicy? policy = null,
        WebViewHostCapabilityBridgeOptions? options = null)
        : this(provider, policy, policyV2: null, options)
    {
    }

    private WebViewHostCapabilityBridge(
        IWebViewHostCapabilityProvider provider,
        IWebViewHostCapabilityPolicy? policy,
        IWebViewHostCapabilityPolicyV2? policyV2,
        WebViewHostCapabilityBridgeOptions? options)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _policy = policy;
        _policyV2 = policyV2;
        _policyEvaluator = new WebViewCapabilityPolicyEvaluator();
        options ??= new WebViewHostCapabilityBridgeOptions();
        _maxSystemIntegrationMetadataTotalLength = ValidateSystemIntegrationMetadataTotalLength(options.SystemIntegrationMetadataTotalLength);
    }

    /// <summary>Create bridge with provider, policy v2 and boundary options.</summary>
    public static WebViewHostCapabilityBridge CreateWithPolicyV2(
        IWebViewHostCapabilityProvider provider,
        IWebViewHostCapabilityPolicyV2 policy,
        WebViewHostCapabilityBridgeOptions? options = null)
        => new(provider, policy: null, policyV2: policy, options);

    /// <summary>Reads text from clipboard.</summary>
    public WebViewHostCapabilityCallResult<string?> ReadClipboardText(
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
        => Execute(
            new WebViewHostCapabilityRequestContext(
                rootWindowId,
                parentWindowId,
                targetWindowId,
                WebViewHostCapabilityOperation.ClipboardReadText),
            () => _provider.ReadClipboardText());

    /// <summary>Writes text to clipboard.</summary>
    public WebViewHostCapabilityCallResult<object?> WriteClipboardText(
        string text,
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Execute<object?>(
            new WebViewHostCapabilityRequestContext(
                rootWindowId,
                parentWindowId,
                targetWindowId,
                WebViewHostCapabilityOperation.ClipboardWriteText),
            () =>
            {
                _provider.WriteClipboardText(text);
                return null;
            },
            requestedAction: "write-text",
            attributes: null);
    }

    /// <summary>Shows open-file dialog.</summary>
    public WebViewHostCapabilityCallResult<WebViewFileDialogResult> ShowOpenFileDialog(
        WebViewOpenFileDialogRequest request,
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute(
            new WebViewHostCapabilityRequestContext(
                rootWindowId,
                parentWindowId,
                targetWindowId,
                WebViewHostCapabilityOperation.FileDialogOpen),
            () => _provider.ShowOpenFileDialog(request),
            requestedAction: "open",
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dialogKind"] = "open",
                ["allowMultiple"] = request.AllowMultiple ? "true" : "false"
            });
    }

    /// <summary>Shows save-file dialog.</summary>
    public WebViewHostCapabilityCallResult<WebViewFileDialogResult> ShowSaveFileDialog(
        WebViewSaveFileDialogRequest request,
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute(
            new WebViewHostCapabilityRequestContext(
                rootWindowId,
                parentWindowId,
                targetWindowId,
                WebViewHostCapabilityOperation.FileDialogSave),
            () => _provider.ShowSaveFileDialog(request),
            requestedAction: "save",
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dialogKind"] = "save"
            });
    }

    /// <summary>Opens URI using external host app/browser.</summary>
    public WebViewHostCapabilityCallResult<object?> OpenExternal(
        Uri uri,
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return Execute<object?>(
            new WebViewHostCapabilityRequestContext(
                rootWindowId,
                parentWindowId,
                targetWindowId,
                WebViewHostCapabilityOperation.ExternalOpen,
                RequestUri: uri),
            () =>
            {
                _provider.OpenExternal(uri);
                return null;
            },
            requestedAction: "open-external",
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scheme"] = uri.Scheme,
                ["host"] = uri.Host
            });
    }

    /// <summary>Shows host notification.</summary>
    public WebViewHostCapabilityCallResult<object?> ShowNotification(
        WebViewNotificationRequest request,
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute<object?>(
            new WebViewHostCapabilityRequestContext(
                rootWindowId,
                parentWindowId,
                targetWindowId,
                WebViewHostCapabilityOperation.NotificationShow),
            () =>
            {
                _provider.ShowNotification(request);
                return null;
            },
            requestedAction: "show-notification",
            attributes: null);
    }

    /// <summary>Applies host app menu model.</summary>
    public WebViewHostCapabilityCallResult<object?> ApplyMenuModel(
        WebViewMenuModelRequest request,
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute<object?>(
            new WebViewHostCapabilityRequestContext(
                rootWindowId,
                parentWindowId,
                targetWindowId,
                WebViewHostCapabilityOperation.MenuApplyModel),
            () =>
            {
                _provider.ApplyMenuModel(request);
                return null;
            },
            requestedAction: "apply-menu-model",
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["target"] = "menu",
                ["itemCount"] = request.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
    }

    /// <summary>Updates host tray state.</summary>
    public WebViewHostCapabilityCallResult<object?> UpdateTrayState(
        WebViewTrayStateRequest request,
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute<object?>(
            new WebViewHostCapabilityRequestContext(
                rootWindowId,
                parentWindowId,
                targetWindowId,
                WebViewHostCapabilityOperation.TrayUpdateState),
            () =>
            {
                _provider.UpdateTrayState(request);
                return null;
            },
            requestedAction: "update-tray-state",
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["target"] = "tray",
                ["hasIconPath"] = string.IsNullOrWhiteSpace(request.IconPath) ? "false" : "true"
            });
    }

    /// <summary>Executes host system action.</summary>
    public WebViewHostCapabilityCallResult<object?> ExecuteSystemAction(
        WebViewSystemActionRequest request,
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute<object?>(
            new WebViewHostCapabilityRequestContext(
                rootWindowId,
                parentWindowId,
                targetWindowId,
                WebViewHostCapabilityOperation.SystemActionExecute),
            () =>
            {
                _provider.ExecuteSystemAction(request);
                return null;
            },
            requestedAction: ToKebabCase(request.Action.ToString()),
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["systemAction"] = ToKebabCase(request.Action.ToString())
            });
    }

    /// <summary>
    /// Dispatches a typed inbound system integration event through policy-first capability flow.
    /// </summary>
    public WebViewHostCapabilityCallResult<WebViewSystemIntegrationEventRequest> DispatchSystemIntegrationEvent(
        WebViewSystemIntegrationEventRequest request,
        Guid rootWindowId,
        Guid? parentWindowId = null,
        Guid? targetWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operation = request.Kind switch
        {
            WebViewSystemIntegrationEventKind.TrayInteracted => WebViewHostCapabilityOperation.TrayInteractionEventDispatch,
            WebViewSystemIntegrationEventKind.MenuItemInvoked => WebViewHostCapabilityOperation.MenuInteractionEventDispatch,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unsupported system integration event kind.")
        };

        var context = new WebViewHostCapabilityRequestContext(
            rootWindowId,
            parentWindowId,
            targetWindowId,
            operation);
        if (!TryValidateSystemIntegrationEventCore(request, out var coreDenyReason))
            return DenyWithDiagnostic<WebViewSystemIntegrationEventRequest>(context, coreDenyReason);
        if (!TryValidateMetadataEnvelope(request.Metadata, out var metadataDenyReason))
            return DenyWithDiagnostic<WebViewSystemIntegrationEventRequest>(context, metadataDenyReason);
        var normalizedRequest = NormalizeSystemIntegrationEventRequest(request);

        return Execute(
            context,
            () =>
            {
                SystemIntegrationEventDispatched?.Invoke(this, normalizedRequest);
                return normalizedRequest;
            },
            requestedAction: request.Kind == WebViewSystemIntegrationEventKind.TrayInteracted ? "dispatch-tray-event" : "dispatch-menu-event",
            attributes: null);
    }

    private WebViewHostCapabilityCallResult<T> Execute<T>(
        in WebViewHostCapabilityRequestContext context,
        Func<T> action,
        string? requestedAction = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        var correlationId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        WebViewHostCapabilityCallResult<T> result;
        var capability = _policyEvaluator.Describe(context);
        var authorizationContext = _policyEvaluator.CreateAuthorizationContext(context, requestedAction, attributes);

        if (!TryEvaluatePolicy(authorizationContext, out var decision, out var policyFailure))
        {
            result = WebViewHostCapabilityCallResult<T>.Failure(
                capability,
                WebViewCapabilityPolicyDecision.Deny("policy-evaluation-failed"),
                policyFailure!,
                wasAuthorized: false);
        }
        else if (!decision.IsAllowed)
        {
            result = WebViewHostCapabilityCallResult<T>.Denied(capability, decision, decision.Reason);
        }
        else
        {
            try
            {
                var value = action();
                result = WebViewHostCapabilityCallResult<T>.Success(capability, decision, value);
            }
            catch (Exception ex)
            {
                if (!WebViewOperationFailure.TryGetCategory(ex, out _))
                    WebViewOperationFailure.SetCategory(ex, WebViewOperationFailureCategory.AdapterFailed);
                result = WebViewHostCapabilityCallResult<T>.Failure(capability, decision, ex, wasAuthorized: true);
            }
        }

        stopwatch.Stop();
        EmitCapabilityDiagnostic(correlationId, authorizationContext, result, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private bool TryEvaluatePolicy(
        in WebViewCapabilityAuthorizationContext context,
        out WebViewCapabilityPolicyDecision decision,
        out Exception? failure)
    {
        try
        {
            decision = _policyEvaluator.Evaluate(_policy, _policyV2, context);
            failure = null;
            return true;
        }
        catch (Exception ex)
        {
            if (!WebViewOperationFailure.TryGetCategory(ex, out _))
                WebViewOperationFailure.SetCategory(ex, WebViewOperationFailureCategory.AdapterFailed);
            decision = WebViewCapabilityPolicyDecision.Deny("policy-evaluation-failed");
            failure = ex;
            return false;
        }
    }

    private bool TryValidateMetadataEnvelope(IReadOnlyDictionary<string, string> metadata, out string denyReason)
    {
        if (metadata.Count > MaxSystemIntegrationMetadataEntries)
        {
            denyReason = SystemIntegrationMetadataEnvelopeInvalid;
            return false;
        }

        var totalLength = 0;

        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > MaxSystemIntegrationMetadataKeyLength)
            {
                denyReason = SystemIntegrationMetadataEnvelopeInvalid;
                return false;
            }
            if (!pair.Key.StartsWith(SystemIntegrationMetadataAllowedPrefix, StringComparison.Ordinal))
            {
                denyReason = SystemIntegrationMetadataNamespaceInvalid;
                return false;
            }
            if (!IsRegisteredSystemIntegrationMetadataKey(pair.Key))
            {
                denyReason = SystemIntegrationMetadataUnregisteredKey;
                return false;
            }

            if (pair.Value is null || pair.Value.Length > MaxSystemIntegrationMetadataValueLength)
            {
                denyReason = SystemIntegrationMetadataEnvelopeInvalid;
                return false;
            }

            totalLength += pair.Key.Length + pair.Value.Length;
            if (totalLength > _maxSystemIntegrationMetadataTotalLength)
            {
                denyReason = SystemIntegrationMetadataBudgetExceeded;
                return false;
            }
        }

        denyReason = string.Empty;
        return true;
    }

    private static bool TryValidateSystemIntegrationEventCore(
        WebViewSystemIntegrationEventRequest request,
        out string denyReason)
    {
        if (string.IsNullOrWhiteSpace(request.Source) ||
            request.OccurredAtUtc == default ||
            string.IsNullOrWhiteSpace(request.ItemId))
        {
            denyReason = SystemIntegrationCoreFieldMissing;
            return false;
        }

        denyReason = string.Empty;
        return true;
    }

    private static bool IsRegisteredSystemIntegrationMetadataKey(string key)
    {
        if (ReservedSystemIntegrationMetadataKeys.Contains(key))
            return true;
        if (!key.StartsWith(SystemIntegrationMetadataExtensionPrefix, StringComparison.Ordinal))
            return false;
        return key.Length > SystemIntegrationMetadataExtensionPrefix.Length;
    }

    private static WebViewSystemIntegrationEventRequest NormalizeSystemIntegrationEventRequest(WebViewSystemIntegrationEventRequest request)
        => new()
        {
            Source = request.Source,
            OccurredAtUtc = NormalizeOccurredAtUtc(request.OccurredAtUtc),
            Kind = request.Kind,
            ItemId = request.ItemId,
            Context = request.Context,
            Metadata = request.Metadata
        };

    private static DateTimeOffset NormalizeOccurredAtUtc(DateTimeOffset occurredAtUtc)
    {
        var utc = occurredAtUtc.ToUniversalTime();
        var normalizedTicks = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond);
        return new DateTimeOffset(normalizedTicks, TimeSpan.Zero);
    }

    private static int ValidateSystemIntegrationMetadataTotalLength(int budget)
    {
        if (budget < WebViewHostCapabilityBridgeOptions.MinSystemIntegrationMetadataTotalLength ||
            budget > WebViewHostCapabilityBridgeOptions.MaxSystemIntegrationMetadataTotalLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budget),
                budget,
                $"System integration metadata total length must be within [{WebViewHostCapabilityBridgeOptions.MinSystemIntegrationMetadataTotalLength}, {WebViewHostCapabilityBridgeOptions.MaxSystemIntegrationMetadataTotalLength}].");
        }

        return budget;
    }

    private WebViewHostCapabilityCallResult<T> DenyWithDiagnostic<T>(
        in WebViewHostCapabilityRequestContext context,
        string denyReason)
    {
        var capability = _policyEvaluator.Describe(context);
        var policyDecision = WebViewCapabilityPolicyDecision.Deny(denyReason);
        var result = WebViewHostCapabilityCallResult<T>.Denied(capability, policyDecision, denyReason);
        var authorizationContext = _policyEvaluator.CreateAuthorizationContext(context);
        EmitCapabilityDiagnostic(
            Guid.NewGuid(),
            authorizationContext,
            result,
            durationMilliseconds: 0);
        return result;
    }

    private void EmitCapabilityDiagnostic<T>(
        Guid correlationId,
        in WebViewCapabilityAuthorizationContext context,
        WebViewHostCapabilityCallResult<T> result,
        long durationMilliseconds)
    {
        WebViewOperationFailureCategory? failureCategory = null;
        if (result.Error is not null && WebViewOperationFailure.TryGetCategory(result.Error, out var category))
            failureCategory = category;

        CapabilityCallCompleted?.Invoke(
            this,
            new WebViewHostCapabilityDiagnosticEventArgs(
                correlationId,
                context.RootWindowId,
                context.ParentWindowId,
                context.TargetWindowId,
                context.Operation,
                context.RequestUri,
                result.Outcome,
                result.WasAuthorized,
                result.DenyReason,
                failureCategory,
                durationMilliseconds,
                result.Capability,
                result.PolicyDecision));
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c) && i > 0)
                chars.Add('-');

            chars.Add(char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }
}
