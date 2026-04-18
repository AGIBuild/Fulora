using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

// ==================== JsImport interfaces for BridgeImportProxy tests ====================

/// <summary>
/// Interface with sync return methods to verify BridgeImportProxy rejects non-Task returns.
/// NOT decorated with [JsImport] to avoid source generator issues with sync signatures.
/// Used directly via DispatchProxy.Create.
/// </summary>
public interface ISyncImport
{
    void FireAndForget(string message);
    void Ping();
    string GetLabel();
}

/// <summary>Interface for Task and Task&lt;T&gt; import proxy coverage.</summary>
[JsImport]
public interface IAsyncImport
{
    Task SendAsync(string data, int retries);
    Task<string> FetchAsync(string key);
}

[JsImport]
public interface IAsyncNoArgsImport
{
    Task PingAsync();
}

/// <summary>Interface with multiple parameters to exercise parameter mapping.</summary>
[JsExport]
public interface IMultiParamExport
{
    Task<string> Greet(string name, int age, bool formal = false);
    Task VoidMethod();
    string SyncMethod(string input);
}

public class FakeMultiParamExport : IMultiParamExport
{
    public Task<string> Greet(string name, int age, bool formal = false)
        => Task.FromResult(formal ? $"Dear {name} ({age})" : $"Hi {name}");

    public Task VoidMethod() => Task.CompletedTask;

    public string SyncMethod(string input) => input.ToUpperInvariant();
}

// ==================== Tests ====================

/// <summary>
/// Supplementary tests to increase code coverage in the Runtime assembly:
/// - BridgeImportProxy (0% → 90%+)
/// - SpaHostingExtensions (0% → 90%+)
/// - RuntimeBridgeService DeserializeParameters edge cases (44.3% → 90%+)
/// - SpaHostingService dev proxy + edge cases (67.7% → 90%+)
/// - WebDialog remaining paths (87.5% → 95%+)
///
/// [Collection] applies to the whole partial class (declared once on this partial).
/// See StatefulIOCollection.cs for the rationale on serializing this class.
/// </summary>
[Collection(StatefulIOCollection.Name)]
public sealed partial class RuntimeCoverageTests
{
    private readonly TestDispatcher _dispatcher = new();

    // ========================= BridgeImportProxy — direct tests =========================

    // ========================= SpaHostingExtensions =========================

    // ========================= SpaHostingService — constructor =========================

    // ========================= SpaHostingService — GetSchemeRegistration =========================

    // ========================= SpaHostingService — TryHandle edge cases =========================

    // ========================= SpaHostingService — DefaultHeaders =========================

    // ========================= SpaHostingService — hashed filename immutable cache =========================

    // ========================= SpaHostingService — dev proxy error path =========================

    // ========================= SpaHostingService — Dispose =========================

    // ========================= SpaHostingService — MIME type edge cases =========================

    // ========================= SpaHostingService — embedded fallback for deep link =========================

    // ========================= RuntimeBridgeService — DeserializeParameters edge cases =========================

    // ========================= RuntimeBridgeService — sync method handler =========================

    // ========================= RuntimeBridgeService — ValidateJsExportAttribute non-interface =========================

    // ========================= RuntimeBridgeService — Dispose clears all =========================

    // ========================= RuntimeBridgeService — Remove removes reflection handler =========================

    // ========================= WebDialog — OpenDevTools / CloseDevTools / IsDevToolsOpen =========================

    // ========================= WebDialog — ZoomFactorChanged event delegation =========================

    // ========================= WebDialog — ContextMenuRequested event unsubscribe =========================

    // ========================= WebDialog — AdapterCreated event =========================

    // ========================= WebDialog — Bridge delegation =========================

    // ========================= WebDialog — double dispose =========================

    // ========================= SpaHostingService — dev proxy non-success fallback path =========================

    // ========================= SpaHostingService — embedded 404 path =========================

    // ========================= RuntimeBridgeService — TargetInvocationException unwrap =========================

    // ========================= RuntimeBridgeService — DeserializeParameters value-type default =========================

    // ========================= RuntimeBridgeService — Source-generated path + RateLimit =========================

    // ========================= RuntimeBridgeService — Rate limit window eviction =========================

    // ========================= SpaHostingService — hashed filename embedded resource cache =========================

    // ========================= SpaHostingService — Dev proxy success path =========================

    // ========================= SpaHostingService — Dev proxy fallback success =========================

    // ========================= SpaHostingService — Dev proxy non-success + fallback also fails =========================

    // ========================= SpaHostingService — Dev proxy + DefaultHeaders =========================

    // ========================= Helpers =========================

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static T CreateProxy<T>(IWebViewRpcService rpc, string serviceName) where T : class
    {
        var proxy = DispatchProxy.Create<T, BridgeImportProxy>();
        var bridgeProxy = (BridgeImportProxy)(object)proxy;
        bridgeProxy.Initialize(rpc, serviceName);
        return proxy;
    }

    private (WebViewCore Core, MockWebViewAdapter Adapter, List<string> Scripts) CreateCoreWithBridge()
    {
        var adapter = MockWebViewAdapter.Create();
        var scripts = new List<string>();
        adapter.ScriptCallback = script => { scripts.Add(script); return null; };
        var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });
        return (core, adapter, scripts);
    }

    private static SpaHostingService CreateEmbeddedSpaService()
    {
        return new SpaHostingService(new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly,
        }, NullTestLogger.Instance);
    }

    private static WebResourceRequestedEventArgs MakeSpaArgs(string uri)
    {
        return new WebResourceRequestedEventArgs(new Uri(uri), "GET");
    }

    // ========================= RuntimeBridgeService — reflection-based Expose =========================
    // These tests use IReflectionExportService from the Testing assembly, which has NO
    // source-generated registration, forcing the reflection fallback in RuntimeBridgeService.

    // ========================= RuntimeBridgeService — reflection-based GetProxy =========================

    // ========================= RuntimeBridgeService — Expose with rate limit via reflection =========================

    // ========================= WebViewCore — DevTools with IDevToolsAdapter mock =========================

    // ========================= WebViewCore — SPA WebResourceRequested integration =========================

    // ==================== Mock RPC service for BridgeImportProxy tests ====================

    /// <summary>Records all RPC invocations for assertions.</summary>
    private sealed class RecordingRpcService : IWebViewRpcService
    {
        public List<(string Method, object? Args)> Invocations { get; } = [];
        public List<(string Method, object? Args)> GenericInvocations { get; } = [];
        public object? NextResult { get; set; }

        private readonly Dictionary<string, Func<JsonElement?, Task<object?>>> _handlers = new();

        public Task<JsonElement> InvokeAsync(string method, object? args = null)
        {
            Invocations.Add((method, args));
            return Task.FromResult(default(JsonElement));
        }

        public Task<T?> InvokeAsync<T>(string method, object? args = null)
        {
            GenericInvocations.Add((method, args));
            if (NextResult is T typed)
                return Task.FromResult<T?>(typed);
            return Task.FromResult<T?>(default);
        }

        public void Handle(string method, Func<JsonElement?, Task<object?>> handler)
        {
            _handlers[method] = handler;
        }

        public void Handle(string method, Func<JsonElement?, object?> handler)
        {
            _handlers[method] = args => Task.FromResult(handler(args));
        }

        public void UnregisterHandler(string method)
        {
            _handlers.Remove(method);
        }

        public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose) { }
    }

    // ==================== Mock DevTools adapter ====================

    private sealed class MockDevToolsAdapter : StubWebViewAdapter, IDevToolsAdapter
    {
        public bool DevToolsOpened { get; private set; }
        public bool DevToolsClosed { get; private set; }
        public bool IsDevToolsOpen { get; private set; }

        public void OpenDevTools() { DevToolsOpened = true; IsDevToolsOpen = true; }
        public void CloseDevTools() { DevToolsClosed = true; IsDevToolsOpen = false; }
    }
}
