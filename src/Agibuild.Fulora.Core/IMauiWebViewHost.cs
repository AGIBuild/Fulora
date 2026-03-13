namespace Agibuild.Fulora;

/// <summary>
/// Contract for hosting an Agibuild.Fulora WebView in a .NET MAUI application.
/// </summary>
public interface IMauiWebViewHost
{
    /// <summary>Gets the underlying WebView instance managed by this host.</summary>
    IWebView WebView { get; }

    /// <summary>Gets the bridge service used for C# ↔ JavaScript communication.</summary>
    IBridgeService Bridge { get; }

    /// <summary>Initializes the WebView host with the specified options.</summary>
    Task InitializeAsync(MauiWebViewHostOptions options, CancellationToken ct = default);

    /// <summary>Navigates the hosted WebView to the specified URI.</summary>
    Task NavigateAsync(Uri uri, CancellationToken ct = default);
}

/// <summary>
/// Options for configuring a MAUI WebView host.
/// </summary>
public sealed class MauiWebViewHostOptions
{
    /// <summary>When true, enables browser developer tools.</summary>
    public bool EnableDevTools { get; set; }

    /// <summary>When true, enables bridge-specific DevTools panel.</summary>
    public bool EnableBridgeDevTools { get; set; }

    /// <summary>Optional SPA hosting configuration for embedded or dev-server assets.</summary>
    public SpaHostingOptions? SpaHosting { get; set; }

    /// <summary>Optional tracer for bridge call observation.</summary>
    public IBridgeTracer? BridgeTracer { get; set; }
}
