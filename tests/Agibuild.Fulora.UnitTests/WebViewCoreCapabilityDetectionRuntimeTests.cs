using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreCapabilityDetectionRuntimeTests
{
    [Fact]
    public void ApplyEnvironmentOptions_invokes_adapter_options()
    {
        var adapter = MockWebViewAdapter.CreateWithOptions();
        var context = WebViewCoreTestContext.Create(
            adapter,
            environmentOptions: new WebViewEnvironmentOptions { CustomUserAgent = "Fulora/Test" });
        var runtime = new WebViewCoreCapabilityDetectionRuntime(context);

        runtime.ApplyEnvironmentOptions();

        Assert.Equal(1, adapter.ApplyOptionsCallCount);
        Assert.Equal("Fulora/Test", adapter.AppliedOptions?.CustomUserAgent);
    }

    [Fact]
    public void RegisterConfiguredCustomSchemes_invokes_custom_scheme_adapter()
    {
        var adapter = MockWebViewAdapter.CreateWithCustomSchemes();
        var context = WebViewCoreTestContext.Create(
            adapter,
            environmentOptions: new WebViewEnvironmentOptions
            {
                CustomSchemes =
                [
                    new CustomSchemeRegistration { SchemeName = "app", HasAuthorityComponent = true, TreatAsSecure = true }
                ]
            });
        var runtime = new WebViewCoreCapabilityDetectionRuntime(context);

        runtime.RegisterConfiguredCustomSchemes();

        Assert.Equal(1, adapter.RegisterCallCount);
        Assert.Single(adapter.RegisteredSchemes!);
    }

    [Fact]
    public void CreateCookieManager_returns_manager()
    {
        var adapter = MockWebViewAdapter.CreateWithCookies();
        var context = WebViewCoreTestContext.Create(adapter);
        var runtime = new WebViewCoreCapabilityDetectionRuntime(context);

        var manager = runtime.CreateCookieManager();

        Assert.NotNull(manager);
    }

    [Fact]
    public void CreateCommandManager_returns_manager()
    {
        var adapter = MockWebViewAdapter.CreateWithCommands();
        var context = WebViewCoreTestContext.Create(adapter);
        var runtime = new WebViewCoreCapabilityDetectionRuntime(context);

        var manager = runtime.CreateCommandManager();

        Assert.NotNull(manager);
    }
}
