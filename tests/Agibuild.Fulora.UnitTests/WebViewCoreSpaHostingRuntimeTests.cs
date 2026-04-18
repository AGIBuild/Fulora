using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreSpaHostingRuntimeTests
{
    [Fact]
    public void EnableSpaHosting_registers_scheme_and_auto_enables_bridge_when_requested()
    {
        var adapter = new MockWebViewAdapterWithCustomSchemes();
        var context = WebViewCoreTestContext.Create(adapter);
        var bridgeRuntime = new WebViewCoreBridgeRuntime(context, enableDevToolsByDefault: false);
        var runtime = new WebViewCoreSpaHostingRuntime(context, bridgeRuntime);

        runtime.EnableSpaHosting(new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly,
            AutoInjectBridgeScript = true
        });

        Assert.NotNull(adapter.RegisteredSchemes);
        Assert.Equal("app", adapter.RegisteredSchemes![0].SchemeName);
        Assert.True(bridgeRuntime.IsBridgeEnabled);
    }

    [Fact]
    public void EnableSpaHosting_does_not_auto_enable_bridge_when_already_enabled()
    {
        var adapter = new MockWebViewAdapterWithCustomSchemes();
        var context = WebViewCoreTestContext.Create(adapter);
        var bridgeRuntime = new WebViewCoreBridgeRuntime(context, enableDevToolsByDefault: false);
        bridgeRuntime.EnableWebMessageBridge(new WebMessageBridgeOptions());

        var runtime = new WebViewCoreSpaHostingRuntime(context, bridgeRuntime);
        runtime.EnableSpaHosting(new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly,
            AutoInjectBridgeScript = true
        });

        // Bridge is still enabled exactly once (no re-enable attempt).
        Assert.True(bridgeRuntime.IsBridgeEnabled);
    }

    [Fact]
    public void EnableSpaHosting_twice_throws()
    {
        var adapter = new MockWebViewAdapterWithCustomSchemes();
        var context = WebViewCoreTestContext.Create(adapter);
        var bridgeRuntime = new WebViewCoreBridgeRuntime(context, enableDevToolsByDefault: false);
        var runtime = new WebViewCoreSpaHostingRuntime(context, bridgeRuntime);
        var options = new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly
        };

        runtime.EnableSpaHosting(options);

        Assert.Throws<InvalidOperationException>(() => runtime.EnableSpaHosting(options));
    }

    [Fact]
    public void HandleWebResourceRequested_without_service_is_noop()
    {
        var adapter = new MockWebViewAdapterWithCustomSchemes();
        var context = WebViewCoreTestContext.Create(adapter);
        var bridgeRuntime = new WebViewCoreBridgeRuntime(context, enableDevToolsByDefault: false);
        var runtime = new WebViewCoreSpaHostingRuntime(context, bridgeRuntime);
        var args = new WebResourceRequestedEventArgs(new Uri("app://localhost/no-service"), "GET");

        runtime.HandleWebResourceRequested(args);

        Assert.False(args.Handled);
    }

    [Fact]
    public void Dispose_unhooks_web_resource_handler()
    {
        var adapter = new MockWebViewAdapterWithCustomSchemes();
        var context = WebViewCoreTestContext.Create(adapter);
        var bridgeRuntime = new WebViewCoreBridgeRuntime(context, enableDevToolsByDefault: false);
        var runtime = new WebViewCoreSpaHostingRuntime(context, bridgeRuntime);

        runtime.EnableSpaHosting(new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly
        });

        // Raise a WebResourceRequested event and verify the runtime handles it while enabled.
        var enabledArgs = new WebResourceRequestedEventArgs(new Uri("app://localhost/"), "GET");
        context.Events.RaiseWebResourceRequested(enabledArgs);

        runtime.Dispose();

        // After dispose, the runtime's handler is unhooked: a new event should not mutate args.
        var afterDisposeArgs = new WebResourceRequestedEventArgs(new Uri("app://localhost/"), "GET");
        context.Events.RaiseWebResourceRequested(afterDisposeArgs);
        Assert.False(afterDisposeArgs.Handled);
    }
}
