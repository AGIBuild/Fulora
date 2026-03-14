using System.Globalization;
using System.Text;

namespace Agibuild.Fulora;

/// <summary>
/// Extension methods for bootstrapping SPA navigation and bridge registration on <see cref="IWebView"/>.
/// </summary>
public static class WebViewBootstrapExtensions
{
    private const string ReadyEventScript =
        "(function(){window.__agWebViewReady=true;window.dispatchEvent(new Event('agWebViewReady'));})()";

    private const string DefaultErrorHtml =
        "<html><body style='font-family:system-ui;padding:2em;color:#333'>" +
        "<h2>Navigation failed</h2><p>{0}</p></body></html>";

    private static readonly CompositeFormat DefaultErrorHtmlFormat = CompositeFormat.Parse(DefaultErrorHtml);

    /// <summary>
    /// Bootstraps WebView using profile options that wrap baseline <see cref="SpaBootstrapOptions"/>
    /// and explicit extension hooks.
    /// </summary>
    public static Task BootstrapSpaProfileAsync(
        this IWebView webView,
        SpaBootstrapProfileOptions profileOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(profileOptions);

        var merged = BuildProfileBootstrapOptions(profileOptions, profileOptions.BootstrapOptions.ServiceProvider);
        RegisterProfileTeardown(webView, profileOptions, merged.ServiceProvider);
        return webView.BootstrapSpaAsync(merged, cancellationToken);
    }

    /// <summary>
    /// Bootstraps the WebView with SPA navigation and bridge service registration in one deterministic call.
    /// <para>
    /// When <see cref="SpaBootstrapOptions.DevServerUrl"/> is set, navigates directly to the dev server.
    /// Otherwise, navigates to the SPA entry point (caller must enable SPA hosting beforehand for production).
    /// </para>
    /// <para>
    /// After successful navigation, configures bridge services via <see cref="SpaBootstrapOptions.ConfigureBridge"/>
    /// and dispatches the <c>agWebViewReady</c> event to the page.
    /// </para>
    /// </summary>
    public static async Task BootstrapSpaAsync(
        this IWebView webView,
        SpaBootstrapOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(options);

        EnsureSpaHostingConfigured(webView, options);
        var targetUri = ResolveTargetUri(options);

        try
        {
            await webView.NavigateAsync(targetUri);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var errorHtml = options.ErrorPageFactory?.Invoke(ex)
                ?? string.Format(CultureInfo.InvariantCulture, DefaultErrorHtmlFormat, System.Net.WebUtility.HtmlEncode(ex.Message));
            await webView.NavigateToStringAsync(errorHtml);
            return;
        }

        options.ConfigureBridge?.Invoke(webView.Bridge, options.ServiceProvider);

        await webView.InvokeScriptAsync(ReadyEventScript);
    }

    internal static SpaBootstrapOptions BuildProfileBootstrapOptions(
        SpaBootstrapProfileOptions profileOptions,
        IServiceProvider? serviceProviderOverride = null)
    {
        ArgumentNullException.ThrowIfNull(profileOptions);
        ArgumentNullException.ThrowIfNull(profileOptions.BootstrapOptions);

        var baseline = profileOptions.BootstrapOptions;
        return new SpaBootstrapOptions
        {
            DevServerUrl = baseline.DevServerUrl,
            EmbeddedResourcePrefix = baseline.EmbeddedResourcePrefix,
            ResourceAssembly = baseline.ResourceAssembly,
            Scheme = baseline.Scheme,
            FallbackDocument = baseline.FallbackDocument,
            ServiceProvider = serviceProviderOverride ?? baseline.ServiceProvider,
            ErrorPageFactory = baseline.ErrorPageFactory,
            ConfigureBridge = (bridge, sp) =>
            {
                baseline.ConfigureBridge?.Invoke(bridge, sp);

                foreach (var extension in profileOptions.Extensions)
                {
                    extension.Configure(bridge, sp, profileOptions.ExceptionScope);
                }
            }
        };
    }

    internal static void RegisterProfileTeardown(
        IWebView webView,
        SpaBootstrapProfileOptions profileOptions,
        IServiceProvider? serviceProvider)
    {
        if (profileOptions.Teardowns.Count == 0)
            return;

        var executionState = 0;
        EventHandler? teardownHandler = null;
        teardownHandler = (_, _) =>
        {
            if (Interlocked.Exchange(ref executionState, 1) != 0)
                return;

            if (teardownHandler is not null)
                webView.AdapterDestroyed -= teardownHandler;

            foreach (var teardown in profileOptions.Teardowns)
                teardown.Execute(serviceProvider, profileOptions.ExceptionScope);
        };

        webView.AdapterDestroyed += teardownHandler;
    }

    private static Uri ResolveTargetUri(SpaBootstrapOptions options)
    {
        if (!string.IsNullOrEmpty(options.DevServerUrl))
            return new Uri(options.DevServerUrl);

        return new Uri($"{options.Scheme}://localhost/{options.FallbackDocument}");
    }

    private static void EnsureSpaHostingConfigured(IWebView webView, SpaBootstrapOptions options)
    {
        var hasEmbeddedPrefix = !string.IsNullOrWhiteSpace(options.EmbeddedResourcePrefix);
        var hasEmbeddedAssembly = options.ResourceAssembly is not null;

        if (hasEmbeddedPrefix != hasEmbeddedAssembly)
        {
            throw new InvalidOperationException(
                "SpaBootstrapOptions.EmbeddedResourcePrefix and ResourceAssembly must be configured together.");
        }

        if (!string.IsNullOrEmpty(options.DevServerUrl) || !hasEmbeddedPrefix)
            return;

        if (webView is not ISpaHostingWebView spaHostingWebView)
        {
            throw new InvalidOperationException(
                $"WebView type '{webView.GetType().Name}' does not support SPA hosting bootstrap.");
        }

        spaHostingWebView.EnableSpaHosting(new SpaHostingOptions
        {
            Scheme = options.Scheme,
            FallbackDocument = options.FallbackDocument,
            EmbeddedResourcePrefix = options.EmbeddedResourcePrefix,
            ResourceAssembly = options.ResourceAssembly,
            AutoInjectBridgeScript = true
        });
    }
}
