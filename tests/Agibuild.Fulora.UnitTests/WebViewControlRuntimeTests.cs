using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewControlRuntimeTests
{
    private readonly TestDispatcher _dispatcher = new();

    [Fact]
    public void BridgeTracer_set_before_core_attach_is_applied_when_core_is_attached()
    {
        var runtime = new WebViewControlRuntime();
        var tracer = NullBridgeTracer.Instance;
        var core = new WebViewCore(MockWebViewAdapter.Create(), _dispatcher);

        runtime.BridgeTracer = tracer;
        runtime.AttachCore(core);

        Assert.Same(tracer, runtime.BridgeTracer);
        Assert.Same(tracer, core.BridgeTracer);
    }

    [Fact]
    public async Task CaptureScreenshotAsync_delegates_to_attached_core()
    {
        var runtime = new WebViewControlRuntime();
        var core = new WebViewCore(MockWebViewAdapter.CreateWithScreenshot(), _dispatcher);

        runtime.AttachCore(core);

        var bytes = await runtime.CaptureScreenshotAsync();

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void SetCustomUserAgent_delegates_to_attached_core()
    {
        var runtime = new WebViewControlRuntime();
        var adapter = MockWebViewAdapter.CreateWithOptions();
        var core = new WebViewCore(adapter, _dispatcher);

        runtime.AttachCore(core);
        runtime.SetCustomUserAgent("Fulora/Test");

        Assert.Equal("Fulora/Test", adapter.AppliedUserAgent);
    }

    [Fact]
    public void Bridge_access_before_core_attach_throws_with_control_ready_guidance()
    {
        var runtime = new WebViewControlRuntime();

        var ex = Assert.Throws<InvalidOperationException>(() => { _ = runtime.Bridge; });

        Assert.Contains("WebView is not yet attached", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bridge_access_after_platform_unavailable_throws_platform_not_supported()
    {
        var runtime = new WebViewControlRuntime();
        runtime.MarkAdapterUnavailable();

        Assert.Throws<PlatformNotSupportedException>(() => { _ = runtime.Bridge; });
    }
}
