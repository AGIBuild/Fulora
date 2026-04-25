namespace Agibuild.Fulora.Security;

/// <summary>
/// Strategy contract that owns every security-relevant decision a
/// platform WebView adapter must make in response to a native security
/// event (currently: server certificate validation failures).
/// </summary>
/// <remarks>
/// <para>
/// All platform adapters route their native security signals through this
/// hook. The adapter never decides "reject vs. proceed" on its own;
/// it only knows how to translate a native event into a
/// <see cref="ServerCertificateErrorContext"/>, ask the hook, and then
/// apply the returned <see cref="NavigationSecurityDecision"/> using its
/// platform-specific cancellation primitive.
/// </para>
/// <para>
/// Internal in 1.x by design. The 2.0 release plan
/// (<c>docs/superpowers/plans/2026-04-23-fulora-v2-public-api-breakage.md</c>)
/// tracks the decision about promoting this surface to <c>public</c>
/// alongside an explicit per-domain trust policy. Until that public
/// surface exists, the only registered implementation is
/// <see cref="DefaultNavigationSecurityHooks"/>, which always returns
/// <see cref="NavigationSecurityDecision.Reject"/>.
/// </para>
/// </remarks>
internal interface INavigationSecurityHooks
{
    /// <summary>
    /// Called by a platform WebView adapter when its native layer reports
    /// a server certificate validation failure for the given context.
    /// Implementations must be safe to invoke from any thread.
    /// </summary>
    /// <param name="context">Structured payload describing the failure. Must not be <see langword="null"/>.</param>
    /// <returns>The decision the adapter must apply.</returns>
    NavigationSecurityDecision OnServerCertificateError(ServerCertificateErrorContext context);
}
