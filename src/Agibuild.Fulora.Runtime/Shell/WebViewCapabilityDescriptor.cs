namespace Agibuild.Fulora.Shell;

/// <summary>
/// Stable capability descriptor used by host-capability policy and diagnostics.
/// </summary>
public sealed record WebViewCapabilityDescriptor(
    string CapabilityId,
    string SourceComponent,
    WebViewHostCapabilityOperation Operation);
