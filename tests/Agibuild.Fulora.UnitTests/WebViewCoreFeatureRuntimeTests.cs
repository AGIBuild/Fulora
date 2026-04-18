using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreFeatureRuntimeTests
{
    [Fact]
    public void Constructor_applies_global_preload_scripts()
    {
        var adapter = MockWebViewAdapter.CreateWithPreload();
        var options = new WebViewEnvironmentOptions
        {
            PreloadScripts = ["console.log('global-a')", "console.log('global-b')"]
        };
        var context = WebViewCoreTestContext.Create(adapter, environmentOptions: options);

        using var runtime = new WebViewCoreFeatureRuntime(context);

        var preloadAdapter = Assert.IsType<MockWebViewAdapterWithPreload>(adapter);
        Assert.Equal(options.PreloadScripts, preloadAdapter.SyncAddedScripts);
    }

    [Fact]
    public void HasDragDropSupport_tracks_adapter_capability()
    {
        var bareAdapter = MockWebViewAdapter.Create();
        using var noDragDropRuntime = new WebViewCoreFeatureRuntime(
            WebViewCoreTestContext.Create(bareAdapter));
        Assert.False(noDragDropRuntime.HasDragDropSupport);

        var dragDropAdapter = MockWebViewAdapter.CreateWithDragDrop();
        using var dragDropRuntime = new WebViewCoreFeatureRuntime(
            WebViewCoreTestContext.Create(dragDropAdapter));
        Assert.True(dragDropRuntime.HasDragDropSupport);
    }

    [Fact]
    public async Task TryGetWebViewHandleAsync_returns_null_after_adapter_destroyed()
    {
        var adapter = MockWebViewAdapter.CreateWithHandle();
        adapter.HandleToReturn = new TestPlatformHandle(new IntPtr(0x42), "feature-runtime");
        var lifecycle = WebViewCoreTestContext.CreateReadyLifecycle();
        var context = WebViewCoreTestContext.Create(adapter, lifecycle: lifecycle);
        using var runtime = new WebViewCoreFeatureRuntime(context);

        var beforeDestroyed = await runtime.TryGetWebViewHandleAsync();
        Assert.NotNull(beforeDestroyed);

        lifecycle.MarkAdapterDestroyedOnce(() => { });

        var afterDestroyed = await runtime.TryGetWebViewHandleAsync();
        Assert.Null(afterDestroyed);
    }
}
