namespace Agibuild.Fulora;

/// <summary>
/// Configuration for the optional service worker that enables offline caching for SPA assets.
/// </summary>
public sealed class ServiceWorkerOptions
{
    /// <summary>Relative URL path to the service worker script.</summary>
    public string ScriptPath { get; set; } = "/sw.js";

    /// <summary>Caching strategy the service worker uses for fetch interception.</summary>
    public ServiceWorkerCacheStrategy CacheStrategy { get; set; } = ServiceWorkerCacheStrategy.NetworkFirst;

    /// <summary>URLs to eagerly cache during service worker installation.</summary>
    public string[] PrecacheUrls { get; set; } = [];

    /// <summary>Name of the Cache Storage bucket used by this service worker.</summary>
    public string CacheName { get; set; } = "agibuild-offline-v1";

    /// <summary>Optional maximum age for cached responses before they are considered stale.</summary>
    public TimeSpan? MaxAge { get; set; }
}

/// <summary>
/// Determines how a service worker intercepts and caches network requests.
/// </summary>
public enum ServiceWorkerCacheStrategy
{
    /// <summary>Serve from cache first; fall back to network on cache miss.</summary>
    CacheFirst,

    /// <summary>Try network first; fall back to cache on network failure.</summary>
    NetworkFirst,

    /// <summary>Serve stale cache immediately while revalidating from network in the background.</summary>
    StaleWhileRevalidate,

    /// <summary>Always fetch from network; never use cache.</summary>
    NetworkOnly,

    /// <summary>Always serve from cache; never fetch from network.</summary>
    CacheOnly
}
