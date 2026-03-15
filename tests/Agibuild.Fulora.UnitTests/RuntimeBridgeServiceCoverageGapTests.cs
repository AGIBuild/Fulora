using System.Reflection;
using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[assembly: Agibuild.Fulora.BridgeProxy(
    typeof(Agibuild.Fulora.UnitTests.IGeneratedProxyMarkerImport),
    typeof(Agibuild.Fulora.UnitTests.GeneratedProxyMarker))]

namespace Agibuild.Fulora.UnitTests;

public interface IGeneratedProxyMarkerImport;

public sealed class GeneratedProxyMarker;

public interface IGeneratedProxyMissingImport;

public sealed class RuntimeBridgeServiceCoverageGapTests
{
    [Fact]
    public void Expose_reflection_path_script_injection_failure_unregisters_handlers()
    {
        var rpc = new RecordingRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => throw new InvalidOperationException("inject-failed"),
            NullLogger.Instance);

        Assert.Throws<InvalidOperationException>(() =>
            bridge.Expose<IReflectionExportService>(new FakeReflectionExportService()));

        Assert.Contains("ReflectionExportService.greet", rpc.RemovedMethods);
        Assert.Contains("ReflectionExportService.saveData", rpc.RemovedMethods);
        Assert.Contains("ReflectionExportService.voidNoArgs", rpc.RemovedMethods);
        Assert.Equal(0, GetExposedServiceCount(bridge));
    }

    [Fact]
    public void Expose_source_generated_path_script_injection_failure_clears_exposed_marker()
    {
        var rpc = new RecordingRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            _ => throw new InvalidOperationException("inject-failed"),
            NullLogger.Instance);

        Assert.Throws<InvalidOperationException>(() =>
            bridge.Expose<IAppService>(new FakeAppService()));

        Assert.Equal(0, GetExposedServiceCount(bridge));
    }

    [Fact]
    public void DeserializeParameters_handles_null_payload_with_defaults()
    {
        var parameters = GetProbeMethod(nameof(DeserializeProbe.NullPayload)).GetParameters();
        var values = InvokeDeserializeParameters(parameters, args: null);

        Assert.Equal(2, values.Length);
        Assert.Null(values[0]);
        Assert.Equal(9, values[1]);
    }

    [Fact]
    public void DeserializeParameters_uses_exact_name_fallback_and_optional_defaults()
    {
        var parameters = GetProbeMethod(nameof(DeserializeProbe.UsesExactAndDefaults)).GetParameters();
        var values = InvokeDeserializeParameters(parameters, ParseJson("""{"UserName":"Alice"}"""));

        Assert.Equal("Alice", values[0]);
        Assert.Equal(3, values[1]);
        Assert.Equal(true, values[2]);
    }

    [Fact]
    public void DeserializeParameters_array_payload_fills_remaining_with_defaults()
    {
        var parameters = GetProbeMethod(nameof(DeserializeProbe.ArrayDefaults)).GetParameters();
        var values = InvokeDeserializeParameters(parameters, ParseJson("""["Bob"]"""));

        Assert.Equal("Bob", values[0]);
        Assert.Equal(7, values[1]);
    }

    [Fact]
    public void DeserializeParameters_non_object_multi_param_payload_returns_empty_slots()
    {
        var parameters = GetProbeMethod(nameof(DeserializeProbe.TwoRequired)).GetParameters();
        var values = InvokeDeserializeParameters(parameters, ParseJson("\"oops\""));

        Assert.Equal(2, values.Length);
        Assert.Null(values[0]);
        Assert.Null(values[1]);
    }

    [Fact]
    public void FindGeneratedProxy_attribute_match_path_returns_null()
    {
        var method = typeof(RuntimeBridgeService).GetMethod(
            "FindGeneratedProxy",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var generic = method!.MakeGenericMethod(typeof(IGeneratedProxyMarkerImport));
        var result = generic.Invoke(null, null);

        Assert.Null(result);
    }

    [Fact]
    public void FindGeneratedProxy_no_attribute_path_returns_null()
    {
        var method = typeof(RuntimeBridgeService).GetMethod(
            "FindGeneratedProxy",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var generic = method!.MakeGenericMethod(typeof(IGeneratedProxyMissingImport));
        var result = generic.Invoke(null, null);

        Assert.Null(result);
    }

    [Fact]
    public async Task MiddlewareRpcWrapper_sync_handle_and_forwarding_paths_are_covered()
    {
        var rpc = new RecordingRpcService();
        var middlewares = new List<IBridgeMiddleware>();
        var proxy = new MiddlewareRpcWrapper(rpc, middlewares, "TestService");

        proxy.Handle("sync.echo", _ => "ok");
        Assert.True(rpc.AsyncHandlers.TryGetValue("sync.echo", out var handler));

        var value = await handler!(null);
        Assert.Equal("ok", value);

        proxy.UnregisterHandler("sync.echo");
        Assert.Contains("sync.echo", rpc.RemovedMethods);

        await proxy.InvokeAsync("remote.call", null, TestContext.Current.CancellationToken);
        await proxy.InvokeAsync<int>("remote.generic", null, TestContext.Current.CancellationToken);

        Assert.Contains("remote.call", rpc.InvokeAsyncCalls);
        Assert.Contains("remote.generic", rpc.InvokeAsyncGenericCalls);
    }

    private static MethodInfo GetProbeMethod(string name)
    {
        var method = typeof(DeserializeProbe).GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        return method!;
    }

    private static object?[] InvokeDeserializeParameters(ParameterInfo[] parameters, JsonElement? args)
    {
        var method = typeof(RuntimeBridgeService).GetMethod(
            "DeserializeParameters",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [parameters, args]);
        return Assert.IsType<object?[]>(result);
    }

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static int GetExposedServiceCount(RuntimeBridgeService bridge)
    {
        var field = typeof(RuntimeBridgeService).GetField(
            "_exportedServices",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);

        var dictionary = field!.GetValue(bridge);
        Assert.NotNull(dictionary);

        var countProp = dictionary!.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(countProp);
        return Assert.IsType<int>(countProp!.GetValue(dictionary));
    }

    private sealed class DeserializeProbe
    {
        public void NullPayload(string UserName, int RetryCount = 9) => _ = (UserName, RetryCount);

        public void UsesExactAndDefaults(string UserName, int RetryCount = 3, bool Enabled = true)
            => _ = (UserName, RetryCount, Enabled);

        public void ArrayDefaults(string UserName, int RetryCount = 7) => _ = (UserName, RetryCount);

        public void TwoRequired(string UserName, int RetryCount) => _ = (UserName, RetryCount);
    }

    private sealed class RecordingRpcService : IWebViewRpcService
    {
        public Dictionary<string, Func<JsonElement?, Task<object?>>> AsyncHandlers { get; } = new(StringComparer.Ordinal);
        public List<string> RemovedMethods { get; } = [];
        public List<string> InvokeAsyncCalls { get; } = [];
        public List<string> InvokeAsyncGenericCalls { get; } = [];

        public void Handle(string method, Func<JsonElement?, Task<object?>> handler)
        {
            AsyncHandlers[method] = handler;
        }

        public void Handle(string method, Func<JsonElement?, object?> handler)
        {
            AsyncHandlers[method] = args => Task.FromResult(handler(args));
        }

        public void UnregisterHandler(string method)
        {
            RemovedMethods.Add(method);
            AsyncHandlers.Remove(method);
        }

        public Task<JsonElement> InvokeAsync(string method, object? args = null)
        {
            InvokeAsyncCalls.Add(method);
            return Task.FromResult(default(JsonElement));
        }

        public Task<T?> InvokeAsync<T>(string method, object? args = null)
        {
            InvokeAsyncGenericCalls.Add(method);
            return Task.FromResult(default(T));
        }

        public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose) { }
    }
}
