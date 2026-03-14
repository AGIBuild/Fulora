using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class BridgeTracerTests
{
    // ==================== NullBridgeTracer ====================

    [Fact]
    public void NullBridgeTracer_singleton_is_not_null()
    {
        Assert.NotNull(NullBridgeTracer.Instance);
    }

    [Fact]
    public void NullBridgeTracer_methods_do_not_throw()
    {
        var tracer = NullBridgeTracer.Instance;
        tracer.OnExportCallStart("svc", "method", "{}");
        tracer.OnExportCallEnd("svc", "method", 42, "string");
        tracer.OnExportCallError("svc", "method", 10, new Exception("test"));
        tracer.OnImportCallStart("svc", "method", null);
        tracer.OnImportCallEnd("svc", "method", 5);
        tracer.OnServiceExposed("svc", 3, true);
        tracer.OnServiceRemoved("svc");
    }

    // ==================== LoggingBridgeTracer ====================

    [Fact]
    public void LoggingBridgeTracer_requires_logger()
    {
        Assert.Throws<ArgumentNullException>(() => new LoggingBridgeTracer(null!));
    }

    [Fact]
    public void LoggingBridgeTracer_methods_do_not_throw()
    {
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggingBridgeTracer>();
        var tracer = new LoggingBridgeTracer(logger);

        tracer.OnExportCallStart("svc", "method", "{}");
        tracer.OnExportCallEnd("svc", "method", 42, "string");
        tracer.OnExportCallError("svc", "method", 10, new Exception("test"));
        tracer.OnImportCallStart("svc", "method", null);
        tracer.OnImportCallEnd("svc", "method", 5);
        tracer.OnServiceExposed("svc", 3, false);
        tracer.OnServiceRemoved("svc");
    }

    // ==================== Custom tracer ====================

    [Fact]
    public void Custom_tracer_receives_events()
    {
        var tracer = new RecordingTracer();

        tracer.OnExportCallStart("Calc", "Add", "{\"a\":1}");
        tracer.OnExportCallEnd("Calc", "Add", 5, "int");
        tracer.OnServiceExposed("Calc", 2, true);

        Assert.Equal(3, tracer.Events.Count);
        Assert.Contains("ExportStart:Calc.Add", tracer.Events);
        Assert.Contains("ExportEnd:Calc.Add:5ms", tracer.Events);
        Assert.Contains("Exposed:Calc:2:SG", tracer.Events);
    }

    private sealed class RecordingTracer : IBridgeTracer
    {
        public List<string> Events { get; } = new();

        public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
            => Events.Add($"ExportStart:{serviceName}.{methodName}");

        public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
            => Events.Add($"ExportEnd:{serviceName}.{methodName}:{elapsedMs}ms");

        public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
            => Events.Add($"ExportError:{serviceName}.{methodName}:{exception.Message}");

        public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
            => Events.Add($"ImportStart:{serviceName}.{methodName}");

        public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
            => Events.Add($"ImportEnd:{serviceName}.{methodName}:{elapsedMs}ms");

        public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
            => Events.Add($"Exposed:{serviceName}:{methodCount}:{(isSourceGenerated ? "SG" : "Reflection")}");

        public void OnServiceRemoved(string serviceName)
            => Events.Add($"Removed:{serviceName}");
    }
}

/// <summary>
/// Tests for TracingRpcWrapper (exercised via RuntimeBridgeService with a tracer).
/// </summary>
public sealed class TracingRpcWrapperTests
{
    [Fact]
    public void Expose_with_tracer_fires_ServiceExposed_event()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        bridge.Expose<IAppService>(new FakeAppService());

        Assert.Contains(tracer.Events, e => e.StartsWith("Exposed:AppService:"));
    }

    [Fact]
    public async Task Expose_with_tracer_wraps_handlers_for_tracing()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        bridge.Expose<IAppService>(new FakeAppService());

        var handler = rpc.Handlers["AppService.getCurrentUser"];
        Assert.NotNull(handler);

        await handler(null);

        Assert.Contains(tracer.Events, e => e.Contains("ExportStart:AppService.getCurrentUser"));
        Assert.Contains(tracer.Events, e => e.Contains("ExportEnd:AppService.getCurrentUser"));
    }

    [Fact]
    public async Task Export_handler_error_fires_ExportCallError()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        bridge.Expose<IAppService>(new ThrowingAppService());

        var handler = rpc.Handlers["AppService.getCurrentUser"];
        await Assert.ThrowsAsync<ArgumentException>(() => handler(null));

        Assert.Contains(tracer.Events, e => e.Contains("ExportError:AppService.getCurrentUser"));
    }

    [Fact]
    public void Remove_with_tracer_fires_ServiceRemoved_event()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        bridge.Expose<IAppService>(new FakeAppService());
        bridge.Remove<IAppService>();

        Assert.Contains(tracer.Events, e => e == "Removed:AppService");
    }

    [Fact]
    public async Task Import_proxy_with_tracer_fires_import_tracing()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        var proxy = bridge.GetProxy<ITracingTestImport>();
        await proxy.DoWork();

        Assert.Contains(tracer.Events, e => e.Contains("ImportStart:"));
        Assert.Contains(tracer.Events, e => e.Contains("ImportEnd:"));
    }

    [Fact]
    public async Task TracingRpcWrapper_InvokeAsync_generic_traces()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        var proxy = bridge.GetProxy<ITracingTestImport>();
        var result = await proxy.GetName();

        Assert.Contains(tracer.Events, e => e.Contains("ImportStart:"));
        Assert.Contains(tracer.Events, e => e.Contains("ImportEnd:"));
    }

    [Fact]
    public async Task TracingRpcWrapper_NotifyAsync_delegates()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        bridge.Expose<IAppService>(new FakeAppService());

        var handler = rpc.Handlers.Values.First();
        await handler(null);

        Assert.True(tracer.Events.Count >= 2);
    }

    [Fact]
    public async Task TracingRpcWrapper_InvokeAsync_with_args_traces_params()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        var proxy = bridge.GetProxy<ITracingTestImport>();
        await proxy.DoWork();

        Assert.Contains(tracer.Events, e => e.Contains("ImportStart:"));
    }

    [Fact]
    public async Task TracingRpcWrapper_InvokeAsync_error_path_traces()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService { ThrowOnInvoke = true };
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        var proxy = bridge.GetProxy<ITracingTestImport>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.DoWork());

        Assert.Contains(tracer.Events, e => e.Contains("ImportStart:"));
        Assert.Contains(tracer.Events, e => e.Contains("ImportEnd:"));
    }

    [Fact]
    public void TracingRpcWrapper_UnregisterHandler_delegates()
    {
        var tracer = new TestTracer();
        var rpc = new StubRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            tracer: tracer);

        bridge.Expose<IAppService>(new FakeAppService());
        bridge.Remove<IAppService>();

        Assert.True(rpc.RemovedMethods.Count > 0);
    }

    [JsImport]
    public interface ITracingTestImport
    {
        Task DoWork();
        Task<string> GetName();
    }

    private sealed class TestTracer : IBridgeTracer
    {
        public List<string> Events { get; } = [];

        public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
            => Events.Add($"ExportStart:{serviceName}.{methodName}");

        public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
            => Events.Add($"ExportEnd:{serviceName}.{methodName}:{elapsedMs}ms");

        public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
            => Events.Add($"ExportError:{serviceName}.{methodName}");

        public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
            => Events.Add($"ImportStart:{serviceName}.{methodName}");

        public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
            => Events.Add($"ImportEnd:{serviceName}.{methodName}:{elapsedMs}ms");

        public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
            => Events.Add($"Exposed:{serviceName}:{methodCount}");

        public void OnServiceRemoved(string serviceName)
            => Events.Add($"Removed:{serviceName}");
    }

    private sealed class StubRpcService : IWebViewRpcService
    {
        public Dictionary<string, Func<System.Text.Json.JsonElement?, Task<object?>>> Handlers { get; } = new();
        public List<string> RemovedMethods { get; } = [];
        public List<(string Method, object? Args)> Invocations { get; } = [];
        public bool ThrowOnInvoke { get; set; }

        public void Handle(string method, Func<System.Text.Json.JsonElement?, Task<object?>> handler)
            => Handlers[method] = handler;

        public void Handle(string method, Func<System.Text.Json.JsonElement?, object?> handler)
            => Handlers[method] = args => Task.FromResult(handler(args));

        public void UnregisterHandler(string method)
        {
            RemovedMethods.Add(method);
            Handlers.Remove(method);
        }

        public Task<System.Text.Json.JsonElement> InvokeAsync(string method, object? args = null)
        {
            Invocations.Add((method, args));
            if (ThrowOnInvoke) throw new InvalidOperationException("stub-invoke-error");
            return Task.FromResult(default(System.Text.Json.JsonElement));
        }

        public Task<T?> InvokeAsync<T>(string method, object? args = null)
        {
            Invocations.Add((method, args));
            if (ThrowOnInvoke) throw new InvalidOperationException("stub-invoke-error");
            return Task.FromResult(default(T));
        }

        public Task<System.Text.Json.JsonElement> InvokeAsync(string method, object? args, CancellationToken ct)
        {
            Invocations.Add((method, args));
            if (ThrowOnInvoke) throw new InvalidOperationException("stub-invoke-error");
            return Task.FromResult(default(System.Text.Json.JsonElement));
        }

        public Task<T?> InvokeAsync<T>(string method, object? args, CancellationToken ct)
        {
            Invocations.Add((method, args));
            if (ThrowOnInvoke) throw new InvalidOperationException("stub-invoke-error");
            return Task.FromResult(default(T));
        }

        public Task NotifyAsync(string method, object? args = null)
        {
            Invocations.Add((method, args));
            return Task.CompletedTask;
        }

        public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose) { }
    }
}
