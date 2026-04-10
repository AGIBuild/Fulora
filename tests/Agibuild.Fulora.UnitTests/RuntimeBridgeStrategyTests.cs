using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class RuntimeBridgeStrategyTests
{
    [Fact]
    public void Expose_uses_first_strategy_that_handles_the_service()
    {
        var skip = new RecordingBridgeStrategy();
        var handled = new RecordingBridgeStrategy
        {
            ExposeResultFactory = implementation => new ExposedService(
                "AppService",
                ["AppService.getCurrentUser"],
                "window.agWebView.bridge.AppService = {};",
                Implementation: implementation)
        };
        var neverReached = new RecordingBridgeStrategy();
        var bridge = CreateBridge(skip, handled, neverReached);
        var implementation = new FakeAppService();

        bridge.Expose<IAppService>(implementation);

        Assert.Equal(1, skip.ExposeCalls);
        Assert.Equal(1, handled.ExposeCalls);
        Assert.Equal(0, neverReached.ExposeCalls);
        Assert.Same(implementation, handled.LastExposedImplementation);
    }

    [Fact]
    public void GetProxy_uses_first_strategy_that_handles_the_service()
    {
        var skip = new RecordingBridgeStrategy();
        var expectedProxy = new AsyncImportProxyStub();
        var handled = new RecordingBridgeStrategy
        {
            ProxyFactory = () => expectedProxy
        };
        var neverReached = new RecordingBridgeStrategy();
        var bridge = CreateBridge(skip, handled, neverReached);

        var proxy = bridge.GetProxy<IAsyncImport>();

        Assert.Same(expectedProxy, proxy);
        Assert.Equal(1, skip.ProxyCalls);
        Assert.Equal(1, handled.ProxyCalls);
        Assert.Equal(0, neverReached.ProxyCalls);
    }

    private static RuntimeBridgeService CreateBridge(params IRuntimeBridgeStrategy[] strategies)
    {
        return new RuntimeBridgeService(
            new NoOpRpcService(),
            _ => Task.FromResult<string?>(null),
            NullLogger.Instance,
            strategies);
    }

    private sealed class RecordingBridgeStrategy : IRuntimeBridgeStrategy
    {
        public int ExposeCalls { get; private set; }

        public int ProxyCalls { get; private set; }

        public object? LastExposedImplementation { get; private set; }

        public Func<object, ExposedService>? ExposeResultFactory { get; init; }

        public Func<object?>? ProxyFactory { get; init; }

        public bool TryExpose<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
            T implementation,
            BridgeOptions? options,
            out ExposedService exposedService) where T : class
        {
            ExposeCalls++;
            LastExposedImplementation = implementation;

            if (ExposeResultFactory is not null)
            {
                exposedService = ExposeResultFactory(implementation);
                return true;
            }

            exposedService = default!;
            return false;
        }

        public bool TryCreateProxy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
            out T? proxy) where T : class
        {
            ProxyCalls++;

            if (ProxyFactory is not null)
            {
                proxy = Assert.IsAssignableFrom<T>(ProxyFactory());
                return true;
            }

            proxy = null;
            return false;
        }
    }

    private sealed class AsyncImportProxyStub : IAsyncImport
    {
        public Task SendAsync(string data, int retries) => Task.CompletedTask;

        public Task<string> FetchAsync(string key) => Task.FromResult(key);
    }

    private sealed class NoOpRpcService : IWebViewRpcService
    {
        public void Handle(string method, Func<JsonElement?, Task<object?>> handler) { }

        public void Handle(string method, Func<JsonElement?, object?> handler) { }

        public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose) { }

        public void UnregisterHandler(string method) { }

        public Task<JsonElement> InvokeAsync(string method, object? args = null) => Task.FromResult(default(JsonElement));

        public Task<T?> InvokeAsync<T>(string method, object? args = null) => Task.FromResult(default(T));
    }
}
