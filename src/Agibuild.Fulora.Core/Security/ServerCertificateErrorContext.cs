namespace Agibuild.Fulora.Security;

/// <summary>
/// Structured payload describing a server certificate validation failure
/// observed by a platform WebView adapter.
/// </summary>
/// <param name="RequestUri">
/// The absolute URI the adapter was attempting to load when the failure
/// was surfaced. Never <see langword="null"/>.
/// </param>
/// <param name="Host">
/// Host portion of <paramref name="RequestUri"/>, duplicated for ease of
/// matching by observability / policy layers that do not want to re-parse
/// the URI. Never <see langword="null"/>.
/// </param>
/// <param name="ErrorSummary">
/// Short human-readable description of the underlying failure
/// (e.g. <c>"CertificateExpired"</c>, <c>"UnknownRoot"</c>). Localisation
/// is deliberately avoided — this string is intended for logs, telemetry,
/// and exception messages, not for end-user UI. Never <see langword="null"/>.
/// </param>
/// <param name="PlatformRawCode">
/// Raw numeric error code as reported by the native layer (e.g. Android
/// <c>SslError.GetPrimaryError()</c>, WebView2 <c>CoreWebView2WebErrorStatus</c>,
/// <c>GTlsCertificateFlags</c>). Stable within a single platform across
/// releases; not comparable across platforms.
/// </param>
/// <param name="CertificateSubject">
/// Distinguished name / subject summary of the leaf certificate when the
/// native layer exposes it. <see langword="null"/> when unavailable.
/// </param>
/// <param name="CertificateIssuer">
/// Distinguished name / issuer summary of the leaf certificate when the
/// native layer exposes it. <see langword="null"/> when unavailable.
/// </param>
/// <param name="ValidFrom">
/// <c>NotBefore</c> validity timestamp of the leaf certificate when the
/// native layer exposes it. <see langword="null"/> when unavailable.
/// </param>
/// <param name="ValidTo">
/// <c>NotAfter</c> validity timestamp of the leaf certificate when the
/// native layer exposes it. <see langword="null"/> when unavailable.
/// </param>
/// <remarks>
/// Adapters populate as many optional fields as the underlying native
/// layer exposes. Consumers must treat every optional field as potentially
/// <see langword="null"/>: there is no platform that guarantees every
/// field is present across every OS version.
/// </remarks>
internal sealed record ServerCertificateErrorContext(
    Uri RequestUri,
    string Host,
    string ErrorSummary,
    int PlatformRawCode,
    string? CertificateSubject = null,
    string? CertificateIssuer = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null);
