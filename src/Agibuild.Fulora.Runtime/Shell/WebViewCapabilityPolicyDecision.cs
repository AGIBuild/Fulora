namespace Agibuild.Fulora.Shell;

/// <summary>
/// Effective capability policy decision kind.
/// </summary>
public enum WebViewCapabilityPolicyDecisionKind
{
    /// <summary>The request is allowed.</summary>
    Allow = 0,
    /// <summary>The request is denied.</summary>
    Deny = 1,
    /// <summary>The request is allowed with additional constraints.</summary>
    AllowWithConstraint = 2
}

/// <summary>
/// Effective capability policy decision surfaced to diagnostics and callers.
/// </summary>
public sealed record WebViewCapabilityPolicyDecision(
    WebViewCapabilityPolicyDecisionKind Kind,
    string? Reason = null,
    IReadOnlyDictionary<string, string>? Constraints = null)
{
    /// <summary>Create an allow decision.</summary>
    public static WebViewCapabilityPolicyDecision Allow()
        => new(WebViewCapabilityPolicyDecisionKind.Allow);

    /// <summary>Create a deny decision.</summary>
    public static WebViewCapabilityPolicyDecision Deny(string? reason = null)
        => new(WebViewCapabilityPolicyDecisionKind.Deny, reason);

    /// <summary>Create an allow-with-constraint decision.</summary>
    public static WebViewCapabilityPolicyDecision AllowWithConstraint(
        IReadOnlyDictionary<string, string> constraints,
        string? reason = null)
        => new(WebViewCapabilityPolicyDecisionKind.AllowWithConstraint, reason, constraints);

    /// <summary>True when the request remains allowed after policy evaluation.</summary>
    public bool IsAllowed => Kind != WebViewCapabilityPolicyDecisionKind.Deny;
}
