using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class HybridE2ETestingTests
{
    [Fact]
    public void BridgeTestTracer_records_export_calls_correctly()
    {
        var tracer = new BridgeTestTracer();
        tracer.OnExportCallStart("AppService", "GetData", "{\"id\":1}");
        tracer.OnExportCallEnd("AppService", "GetData", 42, "string");

        var calls = tracer.GetBridgeCalls();
        Assert.Single(calls);
        var r = calls[0];
        Assert.Equal("AppService", r.ServiceName);
        Assert.Equal("GetData", r.MethodName);
        Assert.Equal(BridgeCallDirection.Export, r.Direction);
        Assert.Equal("{\"id\":1}", r.ParamsJson);
        Assert.Equal("string", r.ResultType);
        Assert.Null(r.ErrorMessage);
        Assert.Equal(42, r.ElapsedMs);
    }

    [Fact]
    public void BridgeTestTracer_records_import_calls_via_OnImportCallStart_End()
    {
        var tracer = new BridgeTestTracer();
        tracer.OnImportCallStart("AuthService", "Login", "{\"user\":\"x\"}");
        tracer.OnImportCallEnd("AuthService", "Login", 15);

        var calls = tracer.GetBridgeCalls();
        Assert.Single(calls);
        var r = calls[0];
        Assert.Equal("AuthService", r.ServiceName);
        Assert.Equal("Login", r.MethodName);
        Assert.Equal(BridgeCallDirection.Import, r.Direction);
        Assert.Equal("{\"user\":\"x\"}", r.ParamsJson);
        Assert.Null(r.ResultType);
        Assert.Null(r.ErrorMessage);
        Assert.Equal(15, r.ElapsedMs);
    }

    [Fact]
    public void BridgeTestTracer_GetBridgeCalls_filters_by_service_name()
    {
        var tracer = new BridgeTestTracer();
        tracer.OnExportCallStart("AppService", "A", null);
        tracer.OnExportCallEnd("AppService", "A", 1, null);
        tracer.OnExportCallStart("AuthService", "B", null);
        tracer.OnExportCallEnd("AuthService", "B", 2, null);
        tracer.OnExportCallStart("AppService", "C", null);
        tracer.OnExportCallEnd("AppService", "C", 3, null);

        var filtered = tracer.GetBridgeCalls("AppService");
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, c => Assert.Equal("AppService", c.ServiceName));
        Assert.Contains(filtered, c => c.MethodName == "A");
        Assert.Contains(filtered, c => c.MethodName == "C");
    }

    [Fact]
    public async Task BridgeTestTracer_WaitForBridgeCallAsync_completes_when_matching_call_arrives()
    {
        var tracer = new BridgeTestTracer();
        var callTask = tracer.WaitForBridgeCallAsync("Svc", "M", TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        tracer.OnImportCallStart("Svc", "M", null);
        tracer.OnImportCallEnd("Svc", "M", 10);

        var record = await callTask;
        Assert.Equal("Svc", record.ServiceName);
        Assert.Equal("M", record.MethodName);
    }

    [Fact]
    public async Task BridgeTestTracer_WaitForBridgeCallAsync_throws_TimeoutException_when_no_call_arrives()
    {
        var tracer = new BridgeTestTracer();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            tracer.WaitForBridgeCallAsync("NonExistent", "Method", TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void BridgeTestTracer_Reset_clears_all_records()
    {
        var tracer = new BridgeTestTracer();
        tracer.OnExportCallStart("S", "M", null);
        tracer.OnExportCallEnd("S", "M", 1, null);
        Assert.Single(tracer.GetBridgeCalls());

        tracer.Reset();
        Assert.Empty(tracer.GetBridgeCalls());
    }

    [Fact]
    public void WebViewTestHandle_EvaluateJsAsync_delegates_to_core()
    {
        var adapter = MockWebViewAdapter.Create();
        adapter.ScriptResult = "\"hello\"";
        var dispatcher = new TestDispatcher();
        var core = new WebViewCore(adapter, dispatcher);
        var tracer = new BridgeTestTracer();
        var handle = new WebViewTestHandle(core, tracer);

        string? result = null;
        DispatcherTestPump.Run(dispatcher, async () =>
        {
            result = await handle.EvaluateJsAsync("test", TestContext.Current.CancellationToken);
        });

        Assert.Equal("\"hello\"", result);
        Assert.Equal("test", adapter.LastScript);

        core.Dispose();
    }

    [Fact]
    public async Task FuloraTestApp_Create_returns_configured_app()
    {
        var app = FuloraTestApp.Create();

        Assert.NotNull(app.Tracer);
        Assert.NotNull(app.Dispatcher);
        Assert.NotNull(app.Core);
        Assert.NotNull(app.GetWebView());

        await app.DisposeAsync();
    }

    [Fact]
    public async Task FuloraTestApp_DisposeAsync_cleans_up()
    {
        var app = FuloraTestApp.Create();
        var core = app.Core;
        Assert.NotNull(core);

        await app.DisposeAsync();

        Assert.Null(app.Core);
        Assert.Throws<ObjectDisposedException>(() => app.GetWebView());
    }
}
