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

    public static bool HasAnyForCurrentPlatform()
        => Providers.Values.Any(static provider => provider.CanHandleCurrentPlatform())
            || Registrations.Keys.Any(k => k.Platform == GetCurrentPlatform());

    public static bool TryCreateForCurrentPlatform(out IWebViewAdapter adapter, out string? failureReason)
    {
        var provider = Providers.Values
            .Where(static provider => provider.CanHandleCurrentPlatform())
            .OrderByDescending(static provider => provider.Priority)
            .FirstOrDefault();

        if (provider is not null)
        {
            adapter = provider.CreateAdapter();
            failureReason = null;
            return true;
        }

        var platform = GetCurrentPlatform();
        var best = Registrations.Values
            .Where(r => r.Platform == platform)
            .OrderByDescending(r => r.Priority)
            .FirstOrDefault();

        if (best is null)
        {
            adapter = null!;
            failureReason = $"No WebView adapter registered for platform '{platform}'.";
            return false;
        }

        adapter = best.Factory();
        failureReason = null;
        return true;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static WebViewAdapterPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WebViewAdapterPlatform.Windows;
        }

        // iOS check must come before macOS because IsMacOS() returns true on Mac Catalyst.
        if (OperatingSystem.IsIOS())
        {
            return WebViewAdapterPlatform.iOS;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WebViewAdapterPlatform.MacOS;
        }

        if (OperatingSystem.IsAndroid())
        {
            return WebViewAdapterPlatform.Android;
        }

        return WebViewAdapterPlatform.Gtk;
    }
}
