namespace Agibuild.Fulora.Security;

/// <summary>
/// The single production implementation of <see cref="INavigationSecurityHooks"/>
/// in 1.x. Always returns <see cref="NavigationSecurityDecision.Reject"/>.
/// </summary>
/// <remarks>
/// <para>
/// Stateless and thread-safe. Adapters resolve the hook through the
/// shared <see cref="Instance"/> singleton; tests may construct their own
/// implementation and inject it through the adapter ctor.
/// </para>
/// <para>
/// Refer to <see cref="INavigationSecurityHooks"/> for the contract and to
/// the navigation-ssl-policy-explicit plan for the rationale behind a
/// hard-coded reject decision in the 1.x series.
/// </para>
/// </remarks>
internal sealed class DefaultNavigationSecurityHooks : INavigationSecurityHooks
{
    /// <summary>Process-wide reusable instance.</summary>
    public static INavigationSecurityHooks Instance { get; } = new DefaultNavigationSecurityHooks();

    private DefaultNavigationSecurityHooks()
    {
    }

    /// <inheritdoc />
    public NavigationSecurityDecision OnServerCertificateError(ServerCertificateErrorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return NavigationSecurityDecision.Reject;
    }
}
