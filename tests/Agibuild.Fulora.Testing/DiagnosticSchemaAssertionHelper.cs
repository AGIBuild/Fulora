using Agibuild.Fulora.Shell;

namespace Agibuild.Fulora.Testing;

/// <summary>
/// Shared diagnostic schema assertions reused across automation lanes.
/// </summary>
public static class DiagnosticSchemaAssertionHelper
{
    /// <summary>Expected schema version for host capability diagnostics.</summary>
    public static int HostCapabilitySchemaVersion => WebViewHostCapabilityDiagnosticEventArgs.CurrentDiagnosticSchemaVersion;

    /// <summary>Expected schema version for session profile diagnostics.</summary>
    public static int SessionProfileSchemaVersion => WebViewSessionPermissionProfileDiagnosticEventArgs.CurrentDiagnosticSchemaVersion;

    /// <summary>Assert host capability diagnostic schema invariants.</summary>
    public static void AssertHostCapabilityDiagnostic(WebViewHostCapabilityDiagnosticEventArgs diagnostic, Guid expectedRootWindowId)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostic.CorrelationId == Guid.Empty)
            throw new InvalidOperationException("Host capability diagnostic CorrelationId must be non-empty.");
        if (diagnostic.RootWindowId != expectedRootWindowId)
            throw new InvalidOperationException("Host capability diagnostic RootWindowId does not match expected value.");
        if (diagnostic.DurationMilliseconds < 0)
            throw new InvalidOperationException("Host capability diagnostic DurationMilliseconds must be non-negative.");
        if (string.IsNullOrWhiteSpace(diagnostic.CapabilityId))
            throw new InvalidOperationException("Host capability diagnostic CapabilityId must be non-empty.");
        if (string.IsNullOrWhiteSpace(diagnostic.SourceComponent))
            throw new InvalidOperationException("Host capability diagnostic SourceComponent must be non-empty.");
        if (diagnostic.DiagnosticSchemaVersion != HostCapabilitySchemaVersion)
            throw new InvalidOperationException("Host capability diagnostic schema version mismatch.");
    }

    /// <summary>Assert session profile diagnostic schema invariants.</summary>
    public static void AssertSessionProfileDiagnostic(WebViewSessionPermissionProfileDiagnosticEventArgs diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostic.WindowId == Guid.Empty)
            throw new InvalidOperationException("Session profile diagnostic WindowId must be non-empty.");
        if (string.IsNullOrWhiteSpace(diagnostic.ProfileIdentity))
            throw new InvalidOperationException("Session profile diagnostic ProfileIdentity must be non-empty.");
        if (diagnostic.DiagnosticSchemaVersion != SessionProfileSchemaVersion)
            throw new InvalidOperationException("Session profile diagnostic schema version mismatch.");
    }
}
