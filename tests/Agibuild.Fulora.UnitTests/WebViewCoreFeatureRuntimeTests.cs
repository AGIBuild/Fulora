using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreFeatureRuntimeTests
{
    private readonly TestDispatcher _dispatcher = new();

    [Fact]
    public void Constructor_applies_global_preload_scripts()
    {
        var host = new TestFeatureHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        var options = new WebViewEnvironmentOptions
        {
            PreloadScripts = ["console.log('global-a')", "console.log('global-b')"]
        };

        using var runtime = new WebViewCoreFeatureRuntime(
            host,
            adapter,
            _dispatcher,
            NullLogger.Instance,
            options);

        var preloadAdapter = Assert.IsType<MockWebViewAdapterWithPreload>(adapter);
        Assert.Equal(options.PreloadScripts, preloadAdapter.SyncAddedScripts);
    }

    [Fact]
    public void HasDragDropSupport_tracks_adapter_capability()
    {
        using var noDragDropRuntime = new WebViewCoreFeatureRuntime(
            new TestFeatureHost(),
            MockWebViewAdapter.Create(),
            _dispatcher,
            NullLogger.Instance,
            new WebViewEnvironmentOptions());
        Assert.False(noDragDropRuntime.HasDragDropSupport);

        using var dragDropRuntime = new WebViewCoreFeatureRuntime(
            new TestFeatureHost(),
            MockWebViewAdapter.CreateWithDragDrop(),
            _dispatcher,
            NullLogger.Instance,
            new WebViewEnvironmentOptions());
        Assert.True(dragDropRuntime.HasDragDropSupport);
    }

    [Fact]
    public async Task TryGetWebViewHandleAsync_returns_null_after_adapter_destroyed()
    {
        var host = new TestFeatureHost();
        var adapter = MockWebViewAdapter.CreateWithHandle();
        adapter.HandleToReturn = new TestPlatformHandle(new IntPtr(0x42), "feature-runtime");
        using var runtime = new WebViewCoreFeatureRuntime(
            host,
            adapter,
            _dispatcher,
            NullLogger.Instance,
            new WebViewEnvironmentOptions());

        var beforeDestroyed = await runtime.TryGetWebViewHandleAsync();
        Assert.NotNull(beforeDestroyed);

        host.IsAdapterDestroyed = true;

        var afterDestroyed = await runtime.TryGetWebViewHandleAsync();
        Assert.Null(afterDestroyed);
    }

    private sealed class TestFeatureHost : IWebViewCoreFeatureHost
    {
        public bool IsDisposed { get; set; }

        public bool IsAdapterDestroyed { get; set; }

        public List<string> ObservedBackgroundOperations { get; } = new();

        public List<double> ZoomChanges { get; } = new();

        public int ContextMenuRaiseCount { get; private set; }

        public int DragEnteredRaiseCount { get; private set; }

        public int DragOverRaiseCount { get; private set; }

        public int DragLeftRaiseCount { get; private set; }

        public int DropCompletedRaiseCount { get; private set; }

        public Task EnqueueOperationAsync(string operationType, Func<Task> func)
            => func();

        public Task<T> EnqueueOperationAsync<T>(string operationType, Func<Task<T>> func)
            => func();

        public void ObserveBackgroundTask(Task task, string operationType)
            => ObservedBackgroundOperations.Add(operationType);

        public void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(IsDisposed, nameof(TestFeatureHost));

        public void RaiseZoomFactorChanged(double zoomFactor)
            => ZoomChanges.Add(zoomFactor);

        public void RaiseContextMenuRequested(ContextMenuRequestedEventArgs args)
            => ContextMenuRaiseCount++;

        public void RaiseDragEntered(DragEventArgs args)
            => DragEnteredRaiseCount++;

        public void RaiseDragOver(DragEventArgs args)
            => DragOverRaiseCount++;

        public void RaiseDragLeft()
            => DragLeftRaiseCount++;

        public void RaiseDropCompleted(DropEventArgs args)
            => DropCompletedRaiseCount++;
    }
}
