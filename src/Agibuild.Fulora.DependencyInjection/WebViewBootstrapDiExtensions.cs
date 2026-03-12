using Microsoft.Extensions.DependencyInjection;

namespace Agibuild.Fulora.DependencyInjection;

/// <summary>
/// DI-aware bootstrap extensions that resolve bridge configuration from the service container.
/// </summary>
public static class WebViewBootstrapDiExtensions
{
    /// <summary>
    /// Bootstraps WebView using profile options and DI-registered bridge configuration actions.
    /// </summary>
    public static Task BootstrapSpaProfileAsync(
        this IWebView webView,
        SpaBootstrapProfileOptions profileOptions,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profileOptions);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var merged = WebViewBootstrapExtensions.BuildProfileBootstrapOptions(profileOptions, serviceProvider);
        WebViewBootstrapExtensions.RegisterProfileTeardown(webView, profileOptions, serviceProvider);
        return webView.BootstrapSpaAsync(merged, serviceProvider, cancellationToken);
    }

    /// <summary>
    /// Bootstraps the WebView using DI-registered bridge configuration actions.
    /// Resolves all <see cref="BridgeConfigurationAction"/> instances from
    /// <see cref="SpaBootstrapOptions.ServiceProvider"/> and composes them with any explicit
    /// <see cref="SpaBootstrapOptions.ConfigureBridge"/> delegate.
    /// </summary>
    public static Task BootstrapSpaAsync(
        this IWebView webView,
        SpaBootstrapOptions options,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var diActions = serviceProvider.GetServices<BridgeConfigurationAction>().ToList();
        var explicitConfigure = options.ConfigureBridge;

        var merged = new SpaBootstrapOptions
        {
            DevServerUrl = options.DevServerUrl,
            EmbeddedResourcePrefix = options.EmbeddedResourcePrefix,
            ResourceAssembly = options.ResourceAssembly,
            Scheme = options.Scheme,
            FallbackDocument = options.FallbackDocument,
            ServiceProvider = serviceProvider,
            ErrorPageFactory = options.ErrorPageFactory,
            ConfigureBridge = (bridge, sp) =>
            {
                foreach (var action in diActions)
                    action.Configure(bridge, sp);

                explicitConfigure?.Invoke(bridge, sp);
            }
        };

        return WebViewBootstrapExtensions.BootstrapSpaAsync(webView, merged, cancellationToken);
    }
}
