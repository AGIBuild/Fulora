using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreCapabilityRuntimeTests
{
    [Fact]
    public async Task OpenDevToolsAsync_delegates_to_feature_operations()
    {
        var feature = new StubFeatureOperations();
        var runtime = new WebViewCoreCapabilityRuntime(feature, new StubBridgeOperations(), null, null);

        await runtime.OpenDevToolsAsync();

        Assert.Equal(1, feature.OpenDevToolsCalls);
    }

    [Fact]
    public void Bridge_and_rpc_access_delegate_to_bridge_operations()
    {
        var bridge = new StubBridgeOperations();
        var runtime = new WebViewCoreCapabilityRuntime(new StubFeatureOperations(), bridge, null, null);

        Assert.Same(bridge.Rpc, runtime.Rpc);
        Assert.Same(bridge.Bridge, runtime.Bridge);
    }

    [Fact]
    public void Cookie_and_command_manager_are_exposed_without_touching_runtime_dependencies()
    {
        var cookies = new StubCookieManager();
        var commands = new StubCommandManager();
        var runtime = new WebViewCoreCapabilityRuntime(new StubFeatureOperations(), new StubBridgeOperations(), cookies, commands);

        Assert.Same(cookies, runtime.TryGetCookieManager());
        Assert.Same(commands, runtime.TryGetCommandManager());
    }

    [Fact]
    public void SetCustomUserAgent_delegates_to_feature_operations()
    {
        var feature = new StubFeatureOperations();
        var runtime = new WebViewCoreCapabilityRuntime(feature, new StubBridgeOperations(), null, null);

        runtime.SetCustomUserAgent("Fulora/Test");

        Assert.Equal("Fulora/Test", feature.LastUserAgent);
    }

    private sealed class StubFeatureOperations : IWebViewCoreFeatureOperations
    {
        public int OpenDevToolsCalls { get; private set; }
        public string? LastUserAgent { get; private set; }

        public Task OpenDevToolsAsync()
        {
            OpenDevToolsCalls++;
            return Task.CompletedTask;
        }

        public Task CloseDevToolsAsync() => Task.CompletedTask;
        public Task<bool> IsDevToolsOpenAsync() => Task.FromResult(false);
        public Task<byte[]> CaptureScreenshotAsync() => Task.FromResult(Array.Empty<byte>());
        public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => Task.FromResult(Array.Empty<byte>());
        public Task<double> GetZoomFactorAsync() => Task.FromResult(1.0);
        public Task SetZoomFactorAsync(double zoomFactor) => Task.CompletedTask;
        public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
            => Task.FromResult(new FindInPageEventArgs { ActiveMatchIndex = 0, TotalMatches = 0 });
        public Task StopFindInPageAsync(bool clearHighlights = true) => Task.CompletedTask;
        public Task<string> AddPreloadScriptAsync(string javaScript) => Task.FromResult("script-id");
        public Task RemovePreloadScriptAsync(string scriptId) => Task.CompletedTask;
        public Task<INativeHandle?> TryGetWebViewHandleAsync() => Task.FromResult<INativeHandle?>(null);
        public void SetCustomUserAgent(string? userAgent) => LastUserAgent = userAgent;
    }

    private sealed class StubBridgeOperations : IWebViewCoreBridgeOperations
    {
        public IWebViewRpcService? Rpc { get; } = new StubRpcService();
        public IBridgeTracer? BridgeTracer { get; set; }
        public IBridgeService Bridge { get; } = new StubBridgeService();
        public void EnableWebMessageBridge(WebMessageBridgeOptions options) { }
        public void DisableWebMessageBridge() { }
        public void ReinjectBridgeStubsIfEnabled() { }
    }

    private sealed class StubRpcService : IWebViewRpcService
    {
        public void Handle(string method, Func<System.Text.Json.JsonElement?, Task<object?>> handler) { }
        public void Handle(string method, Func<System.Text.Json.JsonElement?, object?> handler) { }
        public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose) { }
        public void UnregisterHandler(string method) { }
        public Task<System.Text.Json.JsonElement> InvokeAsync(string method, object? args = null) => Task.FromResult(default(System.Text.Json.JsonElement));
        public Task<T?> InvokeAsync<T>(string method, object? args = null) => Task.FromResult(default(T));
    }

    private sealed class StubBridgeService : IBridgeService
    {
        public void Expose<T>(T implementation, BridgeOptions? options = null) where T : class => throw new NotSupportedException();
        public T GetProxy<T>() where T : class => throw new NotSupportedException();
        public void Remove<T>() where T : class => throw new NotSupportedException();
    }

    private sealed class StubCookieManager : ICookieManager
    {
        public Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri) => Task.FromResult<IReadOnlyList<WebViewCookie>>([]);
        public Task SetCookieAsync(WebViewCookie cookie) => Task.CompletedTask;
        public Task DeleteCookieAsync(WebViewCookie cookie) => Task.CompletedTask;
        public Task ClearAllCookiesAsync() => Task.CompletedTask;
    }

    private sealed class StubCommandManager : ICommandManager
    {
        public Task UndoAsync() => Task.CompletedTask;
        public Task RedoAsync() => Task.CompletedTask;
        public Task CutAsync() => Task.CompletedTask;
        public Task CopyAsync() => Task.CompletedTask;
        public Task PasteAsync() => Task.CompletedTask;
        public Task SelectAllAsync() => Task.CompletedTask;
    }
}
