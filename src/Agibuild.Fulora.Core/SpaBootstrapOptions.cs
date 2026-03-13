using System.Reflection;

namespace Agibuild.Fulora;

/// <summary>
/// Options for <c>WebViewBootstrapExtensions.BootstrapSpaAsync</c>.
/// Encapsulates dev/prod navigation, bridge service registration, and error handling.
/// </summary>
public sealed class SpaBootstrapOptions
{
    /// <summary>
    /// Dev server URL for development mode (e.g. "http://localhost:5173").
    /// When set, the WebView navigates directly to this URL without SPA hosting.
    /// When null, SPA hosting with embedded resources is used (production mode).
    /// </summary>
    public string? DevServerUrl { get; init; }

    /// <summary>
    /// Embedded resource prefix for production SPA hosting (e.g. "wwwroot").
    /// Required when <see cref="DevServerUrl"/> is null.
    /// </summary>
    public string? EmbeddedResourcePrefix { get; init; }

    /// <summary>
    /// Assembly containing embedded resources for production SPA hosting.
    /// Required when <see cref="EmbeddedResourcePrefix"/> is set.
    /// </summary>
    public Assembly? ResourceAssembly { get; init; }

    /// <summary>
    /// Custom scheme name for SPA hosting. Default: "app".
    /// </summary>
    public string Scheme { get; init; } = "app";

    /// <summary>
    /// The SPA entry document. Default: "index.html".
    /// </summary>
    public string FallbackDocument { get; init; } = "index.html";

    /// <summary>
    /// Configures bridge services after navigation completes.
    /// Called with the bridge service and an optional service provider.
    /// Use this to call <c>bridge.Expose&lt;T&gt;()</c> or <c>bridge.UsePlugin&lt;T&gt;(sp)</c>.
    /// </summary>
    public Action<IBridgeService, IServiceProvider?>? ConfigureBridge { get; init; }

    /// <summary>
    /// Optional service provider for DI-aware bridge service factories.
    /// Passed to <see cref="ConfigureBridge"/>.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; init; }

    /// <summary>
    /// Optional HTML content to display when navigation fails.
    /// When null, a default error page is shown.
    /// </summary>
    public Func<Exception, string>? ErrorPageFactory { get; init; }
}
