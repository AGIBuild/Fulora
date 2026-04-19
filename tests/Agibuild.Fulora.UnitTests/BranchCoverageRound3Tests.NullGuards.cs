using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Shell;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

// Branch-coverage tests targeting constructor null guards and other low-leverage uncovered branches
// that landed during the platforms refactor (round-3 follow-up). These tests verify contract
// enforcement (ArgumentNullException paths) and tracer/null-conditional fan-out branches; they are
// intentionally narrow so the rest of the suite stays focused on behavioural coverage.
public sealed partial class BranchCoverageRound3Tests
{
    #region WebViewCoreContext constructor null guards

    [Fact]
    public void WebViewCoreContext_null_adapter_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateContextWithNull("adapter"));
        Assert.Equal("adapter", ex.ParamName);
    }

    [Fact]
    public void WebViewCoreContext_null_dispatcher_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateContextWithNull("dispatcher"));
        Assert.Equal("dispatcher", ex.ParamName);
    }

    [Fact]
    public void WebViewCoreContext_null_logger_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateContextWithNull("logger"));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void WebViewCoreContext_null_environmentOptions_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateContextWithNull("environmentOptions"));
        Assert.Equal("environmentOptions", ex.ParamName);
    }

    [Fact]
    public void WebViewCoreContext_null_lifecycle_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateContextWithNull("lifecycle"));
        Assert.Equal("lifecycle", ex.ParamName);
    }

    [Fact]
    public void WebViewCoreContext_null_events_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateContextWithNull("events"));
        Assert.Equal("events", ex.ParamName);
    }

    [Fact]
    public void WebViewCoreContext_null_operations_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateContextWithNull("operations"));
        Assert.Equal("operations", ex.ParamName);
    }

    private static WebViewCoreContext CreateContextWithNull(string nullParamName)
    {
        var adapter = new MockWebViewAdapter();
        var dispatcher = new TestDispatcher();
        ILogger logger = NullLogger.Instance;
        IWebViewEnvironmentOptions environmentOptions = new WebViewEnvironmentOptions();
        var lifecycle = new WebViewLifecycleStateMachine();
        var events = new WebViewCoreEventHub(new object());
        var operations = new WebViewCoreOperationQueue(lifecycle, dispatcher, logger);
        var capabilities = AdapterCapabilities.From(adapter);

        return new WebViewCoreContext(
            adapter: nullParamName == "adapter" ? null! : adapter,
            capabilities: capabilities,
            dispatcher: nullParamName == "dispatcher" ? null! : dispatcher,
            logger: nullParamName == "logger" ? null! : logger,
            environmentOptions: nullParamName == "environmentOptions" ? null! : environmentOptions,
            lifecycle: nullParamName == "lifecycle" ? null! : lifecycle,
            events: nullParamName == "events" ? null! : events,
            operations: nullParamName == "operations" ? null! : operations,
            channelId: Guid.NewGuid());
    }

    #endregion

    #region WebViewHostCapabilityExecutor constructor null guards

    [Fact]
    public void WebViewHostCapabilityExecutor_null_webView_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateHostExecutorWithNull("webView"));
        Assert.Equal("webView", ex.ParamName);
    }

    [Fact]
    public void WebViewHostCapabilityExecutor_null_options_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateHostExecutorWithNull("options"));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void WebViewHostCapabilityExecutor_null_normalizeMenuModel_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateHostExecutorWithNull("normalizeMenuModel"));
        Assert.Equal("normalizeMenuModel", ex.ParamName);
    }

    [Fact]
    public void WebViewHostCapabilityExecutor_null_tryPruneMenuModel_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateHostExecutorWithNull("tryPruneMenuModel"));
        Assert.Equal("tryPruneMenuModel", ex.ParamName);
    }

    [Fact]
    public void WebViewHostCapabilityExecutor_null_updateEffectiveMenuModel_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateHostExecutorWithNull("updateEffectiveMenuModel"));
        Assert.Equal("updateEffectiveMenuModel", ex.ParamName);
    }

    [Fact]
    public void WebViewHostCapabilityExecutor_null_isSystemActionWhitelisted_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateHostExecutorWithNull("isSystemActionWhitelisted"));
        Assert.Equal("isSystemActionWhitelisted", ex.ParamName);
    }

    [Fact]
    public void WebViewHostCapabilityExecutor_null_reportPolicyFailure_throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateHostExecutorWithNull("reportPolicyFailure"));
        Assert.Equal("reportPolicyFailure", ex.ParamName);
    }

    private static WebViewHostCapabilityExecutor CreateHostExecutorWithNull(string nullParamName)
    {
        var webView = CreateFullWebView();
        var options = new WebViewShellExperienceOptions();
        var rootWindowId = Guid.NewGuid();
        Func<WebViewMenuModelRequest, WebViewMenuModelRequest> normalize = r => r;
        Func<WebViewMenuModelRequest, (WebViewMenuModelRequest? EffectiveMenuModel, WebViewHostCapabilityCallResult<object?>? Result)> tryPrune
            = _ => (null, null);
        Action<WebViewMenuModelRequest> update = _ => { };
        Func<WebViewSystemAction, bool> isWhitelisted = _ => false;
        Action<WebViewShellPolicyDomain, Exception> reportFailure = (_, _) => { };

        return new WebViewHostCapabilityExecutor(
            webView: nullParamName == "webView" ? null! : webView,
            options: nullParamName == "options" ? null! : options,
            rootWindowId: rootWindowId,
            normalizeMenuModel: nullParamName == "normalizeMenuModel" ? null! : normalize,
            tryPruneMenuModel: nullParamName == "tryPruneMenuModel" ? null! : tryPrune,
            updateEffectiveMenuModel: nullParamName == "updateEffectiveMenuModel" ? null! : update,
            isSystemActionWhitelisted: nullParamName == "isSystemActionWhitelisted" ? null! : isWhitelisted,
            reportPolicyFailure: nullParamName == "reportPolicyFailure" ? null! : reportFailure);
    }

    #endregion

    #region WebViewFactory branches

    [Fact]
    public void WebViewFactory_CreateDefault_null_dispatcher_throws()
    {
        Assert.Throws<ArgumentNullException>(() => WebViewFactory.CreateDefault(null!));
    }

    [Fact]
    public void WebViewFactory_CreateDefault_null_dispatcher_with_factory_throws()
    {
        Assert.Throws<ArgumentNullException>(() => WebViewFactory.CreateDefault(null!, NullLoggerFactory.Instance));
    }

    [Fact]
    public void WebViewFactory_CreateDefault_with_loggerFactory_uses_factory_logger()
    {
        EnsureCurrentPlatformAdapterRegistered();
        var dispatcher = new TestDispatcher();
        using var view = WebViewFactory.CreateDefault(dispatcher, NullLoggerFactory.Instance);
        Assert.NotNull(view);
    }

    [Fact]
    public void WebViewFactory_CreateDefault_without_loggerFactory_falls_back_to_null_logger()
    {
        EnsureCurrentPlatformAdapterRegistered();
        var dispatcher = new TestDispatcher();
        using var view = WebViewFactory.CreateDefault(dispatcher);
        Assert.NotNull(view);
    }

    private static void EnsureCurrentPlatformAdapterRegistered()
    {
        // Idempotent: the registry uses TryAdd keyed by (Platform, AdapterId). Re-registering with
        // the same id from multiple tests is a no-op, so we don't need cleanup semantics.
        var registration = new WebViewAdapterRegistration(
            WebViewLegacyAdapterCompatibility.GetCurrentPlatform(),
            "branch-coverage-current-platform-adapter",
            () => new MockWebViewAdapter(),
            Priority: int.MaxValue);
        WebViewAdapterRegistry.Register(registration);
    }

    #endregion

    #region BridgeDebugServer inner-tracer branches

    [Fact]
    public async Task BridgeDebugServer_NullBridgeTracer_inner_collapses_to_no_inner()
    {
        // BridgeDebugServer treats NullBridgeTracer specifically as "no inner tracer". Invoking each
        // export/import/error/lifecycle method exercises the `_inner is null` short-circuit branches.
        var sut = new BridgeDebugServer(port: GetFreePort(), inner: NullBridgeTracer.Instance);
        try
        {
            sut.OnExportCallStart("svc", "m", "{}");
            sut.OnExportCallEnd("svc", "m", 1, "T");
            sut.OnExportCallError("svc", "m", 1, new InvalidOperationException("boom"));
            sut.OnImportCallStart("svc", "m", "{}");
            sut.OnImportCallEnd("svc", "m", 1);
            sut.OnServiceExposed("svc", 3, isSourceGenerated: true);
            sut.OnServiceRemoved("svc");
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task BridgeDebugServer_real_inner_forwards_all_events()
    {
        var inner = new RecordingTracer();
        var sut = new BridgeDebugServer(port: GetFreePort(), inner: inner);
        try
        {
            sut.OnExportCallStart("svc", "m", "{}");
            sut.OnExportCallEnd("svc", "m", 1, "T");
            sut.OnExportCallError("svc", "m", 1, new InvalidOperationException("boom"));
            sut.OnImportCallStart("svc", "m", "{}");
            sut.OnImportCallEnd("svc", "m", 1);
            sut.OnServiceExposed("svc", 3, isSourceGenerated: false);
            sut.OnServiceRemoved("svc");
        }
        finally
        {
            await sut.DisposeAsync();
        }

        Assert.Contains("ExportStart", inner.Events);
        Assert.Contains("ExportEnd", inner.Events);
        Assert.Contains("ExportError", inner.Events);
        Assert.Contains("ImportStart", inner.Events);
        Assert.Contains("ImportEnd", inner.Events);
        Assert.Contains("ServiceExposed", inner.Events);
        Assert.Contains("ServiceRemoved", inner.Events);
    }

    private static int GetFreePort()
    {
        // Bind an OS-allocated ephemeral port, then immediately release it so the SUT can claim it
        // (the BridgeDebugServer itself never calls Start() in these tests, but using a real free
        // port avoids accidental collisions if a parallel test does).
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class RecordingTracer : IBridgeTracer
    {
        public List<string> Events { get; } = new();
        public void OnExportCallStart(string serviceName, string methodName, string? paramsJson) => Events.Add("ExportStart");
        public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType) => Events.Add("ExportEnd");
        public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception) => Events.Add("ExportError");
        public void OnImportCallStart(string serviceName, string methodName, string? paramsJson) => Events.Add("ImportStart");
        public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs) => Events.Add("ImportEnd");
        public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated) => Events.Add("ServiceExposed");
        public void OnServiceRemoved(string serviceName) => Events.Add("ServiceRemoved");
    }

    #endregion

    #region JsonFileConfigProvider branch coverage

    [Fact]
    public void JsonFileConfigProvider_null_path_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonFileConfigProvider(null!));
    }

    [Fact]
    public void JsonFileConfigProvider_missing_file_throws()
    {
        var nonexistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        Assert.Throws<FileNotFoundException>(() => new JsonFileConfigProvider(nonexistent));
    }

    [Fact]
    public async Task JsonFileConfigProvider_exercises_all_value_kinds_and_paths()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        try
        {
            // Cover every IsTruthy / GetValueAsync / IsTruthyString branch with a single fixture.
            var json = """
                {
                  "stringVal": "hello",
                  "objVal": { "nested": "world" },
                  "boolTrue": true,
                  "boolFalse": false,
                  "numberOne": 1,
                  "numberTwo": 2,
                  "truthyString": "yes",
                  "falsyString": "no",
                  "blankString": "   ",
                  "nullVal": null,
                  "arrayVal": [1, 2, 3]
                }
                """;
            File.WriteAllText(path, json);

            var sut = new JsonFileConfigProvider(path);
            var ct = Xunit.TestContext.Current.CancellationToken;

            Assert.Equal("hello", await sut.GetValueAsync("stringVal", ct));
            Assert.NotNull(await sut.GetValueAsync("objVal", ct));
            Assert.Null(await sut.GetValueAsync("missing", ct));

            Assert.Equal("hello", await sut.GetValueAsync<string>("stringVal", ct));
            Assert.Null(await sut.GetValueAsync<int?>("missing", ct));
            Assert.Equal(0, await sut.GetValueAsync<int>("stringVal", ct));

            Assert.True(await sut.IsFeatureEnabledAsync("boolTrue", ct));
            Assert.False(await sut.IsFeatureEnabledAsync("boolFalse", ct));
            Assert.True(await sut.IsFeatureEnabledAsync("numberOne", ct));
            Assert.False(await sut.IsFeatureEnabledAsync("numberTwo", ct));
            Assert.True(await sut.IsFeatureEnabledAsync("truthyString", ct));
            Assert.False(await sut.IsFeatureEnabledAsync("falsyString", ct));
            Assert.False(await sut.IsFeatureEnabledAsync("blankString", ct));
            Assert.False(await sut.IsFeatureEnabledAsync("nullVal", ct));
            Assert.False(await sut.IsFeatureEnabledAsync("arrayVal", ct));
            Assert.False(await sut.IsFeatureEnabledAsync("missing", ct));

            var section = await sut.GetSectionAsync("objVal", ct);
            Assert.NotNull(section);
            Assert.Equal("world", section!["nested"]);
            Assert.Null(await sut.GetSectionAsync("stringVal", ct));
            Assert.Null(await sut.GetSectionAsync("missing", ct));

            await sut.RefreshAsync(ct);
            File.Delete(path);
            await Assert.ThrowsAsync<FileNotFoundException>(() => sut.RefreshAsync(ct));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    #endregion
}
