using System.Collections.Concurrent;

namespace Agibuild.Fulora.Adapters.Abstractions;

internal enum WebViewAdapterPlatform
{
    Windows,
    MacOS,
    Android,
    Gtk,
    iOS
}

internal sealed record WebViewAdapterRegistration(
    WebViewAdapterPlatform Platform,
    string AdapterId,
    Func<IWebViewAdapter> Factory,
    int Priority = 0);

/// <summary>
/// Registry for platform adapter plugins.
/// Intended to be populated by adapter assemblies via module initializers.
/// </summary>
internal static class WebViewAdapterRegistry
{
    private static readonly ConcurrentDictionary<(WebViewAdapterPlatform Platform, string AdapterId), WebViewAdapterRegistration> Registrations = new();
    private static readonly ConcurrentDictionary<string, IWebViewPlatformProvider> Providers = new(StringComparer.Ordinal);

    public static void Register(WebViewAdapterRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registration.AdapterId);
        ArgumentNullException.ThrowIfNull(registration.Factory);

        if (string.IsNullOrWhiteSpace(registration.AdapterId))
        {
            throw new ArgumentException("AdapterId must be non-empty.", nameof(registration));
        }

        Registrations.TryAdd((registration.Platform, registration.AdapterId), registration);
    }

    public static void RegisterProvider(IWebViewPlatformProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.Id);

        Providers.TryAdd(provider.Id, provider);
    }

    internal static void ResetForTests()
    {
        Providers.Clear();
        Registrations.Clear();
    }

    public static bool HasAnyForCurrentPlatform()
    {
        return WebViewAdapterCandidateResolver.HasCandidates(
            Providers.Values.Where(static provider => provider.CanHandleCurrentPlatform()),
            WebViewLegacyAdapterCompatibility.GetCurrentPlatformRegistrations(Registrations.Values));
    }

    public static bool TryCreateForCurrentPlatform(out IWebViewAdapter adapter, out string? failureReason)
    {
        var platform = WebViewLegacyAdapterCompatibility.GetCurrentPlatform();

        if (WebViewAdapterCandidateResolver.TryCreateAdapter(
                Providers.Values.Where(static provider => provider.CanHandleCurrentPlatform()),
                WebViewLegacyAdapterCompatibility.GetCurrentPlatformRegistrations(Registrations.Values),
                $"No WebView adapter registered for platform '{platform}'.",
                out var resolvedAdapter,
                out failureReason))
        {
            adapter = resolvedAdapter!;
            return true;
        }

        adapter = null!;
        return false;
    }
}
