namespace Agibuild.Fulora;

/// <summary>
/// Configuration options for enabling the web message bridge.
/// </summary>
public sealed class WebMessageBridgeOptions
{
    /// <summary>
    /// Allowed origins for WebMessage. Exact string match (e.g. "https://example.com").
    /// </summary>
    public IReadOnlySet<string> AllowedOrigins { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Expected protocol version for incoming bridge envelopes.
    /// Messages with different versions are dropped.
    /// </summary>
    public int ProtocolVersion { get; init; } = 1;

    /// <summary>
    /// Optional sink for diagnostics when inbound messages are dropped by policy.
    /// </summary>
    public IWebMessageDropDiagnosticsSink? DropDiagnosticsSink { get; init; }

    /// <summary>
    /// Optional unified diagnostics sink for runtime and bridge observability.
    /// </summary>
    public IFuloraDiagnosticsSink? DiagnosticsSink { get; init; }

    /// <summary>
    /// When true, bridge error responses include actionable hints in the <c>data.hint</c> field.
    /// Default is false to avoid information leakage in production.
    /// </summary>
    public bool EnableDevToolsDiagnostics { get; init; }
}

internal sealed class DefaultWebMessagePolicy : IWebMessagePolicy
{
    private readonly IReadOnlySet<string> _allowedOrigins;
    private readonly int _protocolVersion;
    private readonly Guid _expectedChannelId;

    public DefaultWebMessagePolicy(IReadOnlySet<string> allowedOrigins, int protocolVersion, Guid expectedChannelId)
    {
        _allowedOrigins = allowedOrigins;
        _protocolVersion = protocolVersion;
        _expectedChannelId = expectedChannelId;
    }

    public WebMessagePolicyDecision Evaluate(in WebMessageEnvelope envelope)
    {
        if (_allowedOrigins.Count > 0 && !_allowedOrigins.Contains(envelope.Origin))
        {
            return WebMessagePolicyDecision.Deny(WebMessageDropReason.OriginNotAllowed);
        }

        if (envelope.ProtocolVersion != _protocolVersion)
        {
            return WebMessagePolicyDecision.Deny(WebMessageDropReason.ProtocolMismatch);
        }

        if (envelope.ChannelId != _expectedChannelId)
        {
            return WebMessagePolicyDecision.Deny(WebMessageDropReason.ChannelMismatch);
        }

        return WebMessagePolicyDecision.Allow();
    }
}
