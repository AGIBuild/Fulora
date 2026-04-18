using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreCapabilityDetectionRuntimeTests
{
    private readonly TestDispatcher _dispatcher = new();

    [Fact]
    public void ApplyEnvironmentOptions_invokes_adapter_options_when_supported()
    {
        var adapter = MockWebViewAdapter.CreateWithOptions();
        var runtime = new WebViewCoreCapabilityDetectionRuntime(
            AdapterCapabilities.From(adapter),
            new WebViewEnvironmentOptions { CustomUserAgent = "Fulora/Test" },
            NullLogger.Instance);

        runtime.ApplyEnvironmentOptions();

        Assert.Equal(1, adapter.ApplyOptionsCallCount);
        Assert.Equal("Fulora/Test", adapter.AppliedOptions?.CustomUserAgent);
    }

    [Fact]
    public void RegisterConfiguredCustomSchemes_invokes_custom_scheme_adapter_when_supported()
    {
        var adapter = MockWebViewAdapter.CreateWithCustomSchemes();
        var runtime = new WebViewCoreCapabilityDetectionRuntime(
            AdapterCapabilities.From(adapter),
            new WebViewEnvironmentOptions
            {
                CustomSchemes =
                [
                    new CustomSchemeRegistration { SchemeName = "app", HasAuthorityComponent = true, TreatAsSecure = true }
                ]
            },
            NullLogger.Instance);

        runtime.RegisterConfiguredCustomSchemes();

        Assert.Equal(1, adapter.RegisterCallCount);
        Assert.Single(adapter.RegisteredSchemes!);
    }

    [Fact]
    public void CreateCookieManager_returns_manager_when_cookie_adapter_is_supported()
    {
        var adapter = MockWebViewAdapter.CreateWithCookies();
        var owner = new WebViewCore(adapter, _dispatcher);
        var runtime = new WebViewCoreCapabilityDetectionRuntime(
            AdapterCapabilities.From(adapter),
            new WebViewEnvironmentOptions(),
            NullLogger.Instance);

        var manager = runtime.CreateCookieManager(owner);

        Assert.NotNull(manager);
    }

    [Fact]
    public void CreateCommandManager_returns_manager_when_command_adapter_is_supported()
    {
        var adapter = MockWebViewAdapter.CreateWithCommands();
        var owner = new WebViewCore(adapter, _dispatcher);
        var runtime = new WebViewCoreCapabilityDetectionRuntime(
            AdapterCapabilities.From(adapter),
            new WebViewEnvironmentOptions(),
            NullLogger.Instance);

        var manager = runtime.CreateCommandManager(owner);

        Assert.NotNull(manager);
    }
}
