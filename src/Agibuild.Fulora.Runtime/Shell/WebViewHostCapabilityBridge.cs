using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Agibuild.Fulora.Shell;

/// <summary>
/// Typed host capability operations available through the shell bridge.
/// </summary>
public enum WebViewHostCapabilityOperation
{
    /// <summary>Read text from host clipboard.</summary>
    ClipboardReadText = 0,
    /// <summary>Write text to host clipboard.</summary>
    ClipboardWriteText = 1,
    /// <summary>Open-file picker operation.</summary>
    FileDialogOpen = 2,
    /// <summary>Save-file picker operation.</summary>
    FileDialogSave = 3,
    /// <summary>Open URI in external application/browser.</summary>
    ExternalOpen = 4,
    /// <summary>Show a host notification.</summary>
    NotificationShow = 5,
    /// <summary>Apply host app menu model.</summary>
    MenuApplyModel = 6,
    /// <summary>Update host tray state.</summary>
    TrayUpdateState = 7,
    /// <summary>Execute host system action.</summary>
    SystemActionExecute = 8,
    /// <summary>Dispatch tray interaction event to web pipeline.</summary>
    TrayInteractionEventDispatch = 9,
    /// <summary>Dispatch menu interaction event to web pipeline.</summary>
    MenuInteractionEventDispatch = 10,
    /// <summary>Register a global keyboard shortcut at the OS level.</summary>
    GlobalShortcutRegister = 11
}

/// <summary>
/// Request context for host capability authorization.
/// </summary>
public readonly record struct WebViewHostCapabilityRequestContext(
    Guid RootWindowId,
    Guid? ParentWindowId,
    Guid? TargetWindowId,
    WebViewHostCapabilityOperation Operation,
    Uri? RequestUri = null);

/// <summary>
/// Authorization decision kind for a capability request.
/// </summary>
public enum WebViewHostCapabilityDecisionKind
{
    /// <summary>Request is allowed.</summary>
    Allow = 0,
    /// <summary>Request is denied.</summary>
    Deny = 1
}

/// <summary>
/// Authorization decision for a capability request.
/// </summary>
public readonly record struct WebViewHostCapabilityDecision(
    WebViewHostCapabilityDecisionKind Kind,
    string? Reason = null)
{
    /// <summary>Create an allow decision.</summary>
    public static WebViewHostCapabilityDecision Allow()
        => new(WebViewHostCapabilityDecisionKind.Allow);

    /// <summary>Create a deny decision.</summary>
    public static WebViewHostCapabilityDecision Deny(string? reason = null)
        => new(WebViewHostCapabilityDecisionKind.Deny, reason);

    /// <summary>
    /// True when this request is allowed.
    /// </summary>
    public bool IsAllowed => Kind == WebViewHostCapabilityDecisionKind.Allow;
}

/// <summary>
/// Deterministic outcome model for host capability calls.
/// </summary>
public enum WebViewHostCapabilityCallOutcome
{
    /// <summary>Capability is authorized and executed successfully.</summary>
    Allow = 0,
    /// <summary>Capability is denied by policy before provider execution.</summary>
    Deny = 1,
    /// <summary>Capability flow failed deterministically (policy/provider error).</summary>
    Failure = 2
}

/// <summary>
/// Host capability authorization policy.
/// </summary>
public interface IWebViewHostCapabilityPolicy
{
    /// <summary>Evaluates a capability request context.</summary>
    WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context);
}

/// <summary>
/// Request payload for open-file dialog.
/// </summary>
public sealed class WebViewOpenFileDialogRequest
{
    /// <summary>Dialog title.</summary>
    public string? Title { get; init; }
    /// <summary>Whether multiple selection is allowed.</summary>
    public bool AllowMultiple { get; init; }
}

/// <summary>
/// Request payload for save-file dialog.
/// </summary>
public sealed class WebViewSaveFileDialogRequest
{
    /// <summary>Dialog title.</summary>
    public string? Title { get; init; }
    /// <summary>Suggested initial file name.</summary>
    public string? SuggestedFileName { get; init; }
}

/// <summary>
/// Request payload for host notification.
/// </summary>
public sealed class WebViewNotificationRequest
{
    /// <summary>Notification title.</summary>
    public required string Title { get; init; }
    /// <summary>Notification body.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Typed menu model payload for host app-shell integration.
/// </summary>
public sealed class WebViewMenuModelRequest
{
    /// <summary>Top-level menu items.</summary>
    public IReadOnlyList<WebViewMenuItemModel> Items { get; init; } = [];
}

/// <summary>
/// Typed menu item model.
/// </summary>
public sealed class WebViewMenuItemModel
{
    /// <summary>Stable item id.</summary>
    public required string Id { get; init; }
    /// <summary>User-visible label.</summary>
    public required string Label { get; init; }
    /// <summary>Whether this item is enabled.</summary>
    public bool IsEnabled { get; init; } = true;
    /// <summary>Nested child menu items.</summary>
    public IReadOnlyList<WebViewMenuItemModel> Children { get; init; } = [];
}

/// <summary>
/// Typed tray state payload for host app-shell integration.
/// </summary>
public sealed class WebViewTrayStateRequest
{
    /// <summary>Whether tray entry should be visible.</summary>
    public bool IsVisible { get; init; }
    /// <summary>Optional tray tooltip text.</summary>
    public string? Tooltip { get; init; }
    /// <summary>Optional tray icon path.</summary>
    public string? IconPath { get; init; }
}

/// <summary>
/// Supported typed system actions.
/// </summary>
public enum WebViewSystemAction
{
    /// <summary>Quit application.</summary>
    Quit = 0,
    /// <summary>Restart application.</summary>
    Restart = 1,
    /// <summary>Focus main window.</summary>
    FocusMainWindow = 2,
    /// <summary>Show application about dialog.</summary>
    ShowAbout = 3
}

/// <summary>
/// Typed system integration event kinds emitted from host to web pipeline.
/// </summary>
public enum WebViewSystemIntegrationEventKind
{
    /// <summary>Tray icon was interacted with.</summary>
    TrayInteracted = 0,
    /// <summary>Menu item was invoked.</summary>
    MenuItemInvoked = 1
}

/// <summary>
/// Typed inbound system integration event payload.
/// </summary>
public sealed class WebViewSystemIntegrationEventRequest
{
    /// <summary>Stable host-originated event source identity.</summary>
    public string? Source { get; init; }
    /// <summary>UTC timestamp of when the host event occurred.</summary>
    public DateTimeOffset OccurredAtUtc { get; init; }
    /// <summary>Event kind.</summary>
    public WebViewSystemIntegrationEventKind Kind { get; init; }
    /// <summary>Optional item id for menu/tray events.</summary>
    public string? ItemId { get; init; }
    /// <summary>Optional context payload for diagnostics and UI reaction.</summary>
    public string? Context { get; init; }
    /// <summary>
    /// Optional bounded metadata envelope. Keys and values must satisfy runtime schema limits.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Typed request payload for host system action execution.
/// </summary>
public sealed class WebViewSystemActionRequest
{
    /// <summary>Action to execute.</summary>
    public WebViewSystemAction Action { get; init; }
}

/// <summary>
/// Result of file dialog operations.
/// </summary>
public sealed class WebViewFileDialogResult
{
    /// <summary>Whether user canceled the dialog.</summary>
    public bool IsCanceled { get; init; }
    /// <summary>Selected file paths.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
}

/// <summary>
/// Host capability provider implementation.
/// </summary>
public interface IWebViewHostCapabilityProvider
{
    /// <summary>Reads text from host clipboard.</summary>
    string? ReadClipboardText();
    /// <summary>Writes text to host clipboard.</summary>
    void WriteClipboardText(string text);
    /// <summary>Shows open-file dialog.</summary>
    WebViewFileDialogResult ShowOpenFileDialog(WebViewOpenFileDialogRequest request);
    /// <summary>Shows save-file dialog.</summary>
    WebViewFileDialogResult ShowSaveFileDialog(WebViewSaveFileDialogRequest request);
    /// <summary>Opens URI using external host app/browser.</summary>
    void OpenExternal(Uri uri);
    /// <summary>Shows host notification.</summary>
    void ShowNotification(WebViewNotificationRequest request);
    /// <summary>Applies host app menu model.</summary>
    void ApplyMenuModel(WebViewMenuModelRequest request);
    /// <summary>Updates host tray state.</summary>
    void UpdateTrayState(WebViewTrayStateRequest request);
    /// <summary>Executes host system action.</summary>
    void ExecuteSystemAction(WebViewSystemActionRequest request);
}

/// <summary>
/// Typed result envelope for host capability calls.
/// </summary>
public sealed class WebViewHostCapabilityCallResult<T>
{
    private WebViewHostCapabilityCallResult(
        WebViewHostCapabilityCallOutcome outcome,
        bool wasAuthorized,
        T? value,
        string? denyReason,
        Exception? error,
        WebViewCapabilityDescriptor capability,
        WebViewCapabilityPolicyDecision policyDecision)
    {
        Outcome = outcome;
        WasAuthorized = wasAuthorized;
        Value = value;
        DenyReason = denyReason;
        Error = error;
        Capability = capability;
        PolicyDecision = policyDecision;
    }

    /// <summary>Deterministic capability outcome.</summary>
    public WebViewHostCapabilityCallOutcome Outcome { get; }
    /// <summary>Whether policy authorization succeeded before execution.</summary>
    public bool WasAuthorized { get; }
    /// <summary>Whether the capability request was authorized.</summary>
    public bool IsAllowed => WasAuthorized;
    /// <summary>Whether the authorized operation completed successfully.</summary>
    public bool IsSuccess => Outcome == WebViewHostCapabilityCallOutcome.Allow;
    /// <summary>Typed return value.</summary>
    public T? Value { get; }
    /// <summary>Deny reason when request is denied.</summary>
    public string? DenyReason { get; }
    /// <summary>Operation error when execution fails.</summary>
    public Exception? Error { get; }
    /// <summary>Stable capability descriptor for this result.</summary>
    public WebViewCapabilityDescriptor Capability { get; }
    /// <summary>Stable capability identifier for this result.</summary>
    public string CapabilityId => Capability.CapabilityId;
    /// <summary>Stable source component token for this result.</summary>
    public string SourceComponent => Capability.SourceComponent;
    /// <summary>Effective policy decision for this result.</summary>
    public WebViewCapabilityPolicyDecision PolicyDecision { get; }

    internal static WebViewHostCapabilityCallResult<T> Success(
        WebViewCapabilityDescriptor capability,
        WebViewCapabilityPolicyDecision policyDecision,
        T? value)
        => new(WebViewHostCapabilityCallOutcome.Allow, wasAuthorized: true, value, denyReason: null, error: null, capability, policyDecision);

    internal static WebViewHostCapabilityCallResult<T> Denied(
        WebViewCapabilityDescriptor capability,
        WebViewCapabilityPolicyDecision policyDecision,
        string? reason = null)
        => new(WebViewHostCapabilityCallOutcome.Deny, wasAuthorized: false, value: default, denyReason: reason ?? policyDecision.Reason, error: null, capability, policyDecision);

    internal static WebViewHostCapabilityCallResult<T> Failure(
        WebViewCapabilityDescriptor capability,
        WebViewCapabilityPolicyDecision policyDecision,
        Exception error,
        bool wasAuthorized)
        => new(WebViewHostCapabilityCallOutcome.Failure, wasAuthorized, value: default, denyReason: null, error, capability, policyDecision);
}

/// <summary>
/// Structured diagnostic payload for a completed host capability call.
/// </summary>
public sealed class WebViewHostCapabilityDiagnosticEventArgs : EventArgs
{
    /// <summary>Current schema version for host capability diagnostics.</summary>
    public const int CurrentDiagnosticSchemaVersion = 1;

    /// <summary>Create diagnostic payload.</summary>
    public WebViewHostCapabilityDiagnosticEventArgs(
        Guid correlationId,
        Guid rootWindowId,
        Guid? parentWindowId,
        Guid? targetWindowId,
        WebViewHostCapabilityOperation operation,
        Uri? requestUri,
        WebViewHostCapabilityCallOutcome outcome,
        bool wasAuthorized,
        string? denyReason,
        WebViewOperationFailureCategory? failureCategory,
        long durationMilliseconds,
        WebViewCapabilityDescriptor? capability = null,
        WebViewCapabilityPolicyDecision? policyDecision = null)
    {
        CorrelationId = correlationId;
        RootWindowId = rootWindowId;
        ParentWindowId = parentWindowId;
        TargetWindowId = targetWindowId;
        Operation = operation;
        RequestUri = requestUri;
        Outcome = outcome;
        WasAuthorized = wasAuthorized;
        DenyReason = denyReason;
        FailureCategory = failureCategory;
        DurationMilliseconds = durationMilliseconds;
        Capability = capability ?? WebViewCapabilityPolicyEvaluator.Describe(operation);
        PolicyDecision = policyDecision ?? InferPolicyDecision(outcome, wasAuthorized, denyReason);
        DiagnosticSchemaVersion = CurrentDiagnosticSchemaVersion;
    }

    /// <summary>Stable call correlation id.</summary>
    public Guid CorrelationId { get; }
    /// <summary>Root shell window id.</summary>
    public Guid RootWindowId { get; }
    /// <summary>Optional parent window id.</summary>
    public Guid? ParentWindowId { get; }
    /// <summary>Optional target window id.</summary>
    public Guid? TargetWindowId { get; }
    /// <summary>Capability operation.</summary>
    public WebViewHostCapabilityOperation Operation { get; }
    /// <summary>Optional request URI for URI-bound operations.</summary>
    public Uri? RequestUri { get; }
    /// <summary>Deterministic capability outcome.</summary>
    public WebViewHostCapabilityCallOutcome Outcome { get; }
    /// <summary>Whether policy authorization succeeded before execution.</summary>
    public bool WasAuthorized { get; }
    /// <summary>Deny reason when outcome is deny.</summary>
    public string? DenyReason { get; }
    /// <summary>Failure category when outcome is failure.</summary>
    public WebViewOperationFailureCategory? FailureCategory { get; }
    /// <summary>Elapsed duration in milliseconds.</summary>
    public long DurationMilliseconds { get; }
    /// <summary>Stable capability descriptor.</summary>
    public WebViewCapabilityDescriptor Capability { get; }
    /// <summary>Stable capability identifier.</summary>
    public string CapabilityId => Capability.CapabilityId;
    /// <summary>Stable source component token.</summary>
    public string SourceComponent => Capability.SourceComponent;
    /// <summary>Effective policy decision.</summary>
    public WebViewCapabilityPolicyDecision PolicyDecision { get; }
    /// <summary>Diagnostic payload schema version.</summary>
    public int DiagnosticSchemaVersion { get; }

    /// <summary>
    /// Converts diagnostic event to a stable export record for machine-readable pipelines.
    /// </summary>
    public WebViewHostCapabilityDiagnosticExportRecord ToExportRecord()
        => new(
            schemaVersion: DiagnosticSchemaVersion,
            correlationId: CorrelationId.ToString("D"),
            rootWindowId: RootWindowId.ToString("D"),
            parentWindowId: ParentWindowId?.ToString("D"),
            targetWindowId: TargetWindowId?.ToString("D"),
            operation: ToKebabCase(Operation.ToString()),
            requestUri: RequestUri?.AbsoluteUri,
            capabilityId: CapabilityId,
            sourceComponent: SourceComponent,
            policyDecision: ToKebabCase(PolicyDecision.Kind.ToString()),
            outcome: ToKebabCase(Outcome.ToString()),
            wasAuthorized: WasAuthorized,
            denyReason: DenyReason,
            failureCategory: FailureCategory is null ? null : ToKebabCase(FailureCategory.Value.ToString()),
            durationMilliseconds: DurationMilliseconds);

    private static WebViewCapabilityPolicyDecision InferPolicyDecision(
        WebViewHostCapabilityCallOutcome outcome,
        bool wasAuthorized,
        string? denyReason)
        => outcome switch
        {
            WebViewHostCapabilityCallOutcome.Deny => WebViewCapabilityPolicyDecision.Deny(denyReason),
            WebViewHostCapabilityCallOutcome.Allow => WebViewCapabilityPolicyDecision.Allow(),
            WebViewHostCapabilityCallOutcome.Failure when wasAuthorized => WebViewCapabilityPolicyDecision.Allow(),
            _ => WebViewCapabilityPolicyDecision.Deny(denyReason ?? "policy-evaluation-failed")
        };

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

/// <summary>
/// Stable export payload for host capability diagnostics.
/// </summary>
public sealed class WebViewHostCapabilityDiagnosticExportRecord
{
    /// <summary>Create export record.</summary>
    public WebViewHostCapabilityDiagnosticExportRecord(
        int schemaVersion,
        string correlationId,
        string rootWindowId,
        string? parentWindowId,
        string? targetWindowId,
        string operation,
        string? requestUri,
        string capabilityId,
        string sourceComponent,
        string policyDecision,
        string outcome,
        bool wasAuthorized,
        string? denyReason,
        string? failureCategory,
        long durationMilliseconds)
    {
        SchemaVersion = schemaVersion;
        CorrelationId = correlationId;
        RootWindowId = rootWindowId;
        ParentWindowId = parentWindowId;
        TargetWindowId = targetWindowId;
        Operation = operation;
        RequestUri = requestUri;
        CapabilityId = capabilityId;
        SourceComponent = sourceComponent;
        PolicyDecision = policyDecision;
        Outcome = outcome;
        WasAuthorized = wasAuthorized;
        DenyReason = denyReason;
        FailureCategory = failureCategory;
        DurationMilliseconds = durationMilliseconds;
    }

    /// <summary>Diagnostic schema version.</summary>
    public int SchemaVersion { get; }
    /// <summary>Correlation id as canonical guid string.</summary>
    public string CorrelationId { get; }
    /// <summary>Root window id as canonical guid string.</summary>
    public string RootWindowId { get; }
    /// <summary>Optional parent window id as canonical guid string.</summary>
    public string? ParentWindowId { get; }
    /// <summary>Optional target window id as canonical guid string.</summary>
    public string? TargetWindowId { get; }
    /// <summary>Operation token in kebab-case.</summary>
    public string Operation { get; }
    /// <summary>Optional request URI.</summary>
    public string? RequestUri { get; }
    /// <summary>Stable capability identifier.</summary>
    public string CapabilityId { get; }
    /// <summary>Stable source component token.</summary>
    public string SourceComponent { get; }
    /// <summary>Effective policy decision token in kebab-case.</summary>
    public string PolicyDecision { get; }
    /// <summary>Outcome token in kebab-case.</summary>
    public string Outcome { get; }
    /// <summary>Whether authorization passed.</summary>
    public bool WasAuthorized { get; }
    /// <summary>Deny reason when outcome is deny.</summary>
    public string? DenyReason { get; }
    /// <summary>Failure category when outcome is failure.</summary>
    public string? FailureCategory { get; }
    /// <summary>Elapsed duration in milliseconds.</summary>
    public long DurationMilliseconds { get; }
}

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
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _policy = policy;
        _policyEvaluator = new WebViewCapabilityPolicyEvaluator();
        options ??= new WebViewHostCapabilityBridgeOptions();
        _maxSystemIntegrationMetadataTotalLength = ValidateSystemIntegrationMetadataTotalLength(options.SystemIntegrationMetadataTotalLength);
    }

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
            });
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
            () => _provider.ShowOpenFileDialog(request));
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
            () => _provider.ShowSaveFileDialog(request));
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
            });
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
            });
    }

    private WebViewHostCapabilityCallResult<T> Execute<T>(
        in WebViewHostCapabilityRequestContext context,
        Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var correlationId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        WebViewHostCapabilityCallResult<T> result;
        var capability = _policyEvaluator.Describe(context);

        if (!TryEvaluatePolicy(context, out var decision, out var policyFailure))
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
        EmitCapabilityDiagnostic(correlationId, context, result, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private bool TryEvaluatePolicy(
        in WebViewHostCapabilityRequestContext context,
        out WebViewCapabilityPolicyDecision decision,
        out Exception? failure)
    {
        try
        {
            decision = _policyEvaluator.Evaluate(_policy, context);
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
        EmitCapabilityDiagnostic(
            Guid.NewGuid(),
            context,
            result,
            durationMilliseconds: 0);
        return result;
    }

    private void EmitCapabilityDiagnostic<T>(
        Guid correlationId,
        in WebViewHostCapabilityRequestContext context,
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
}
