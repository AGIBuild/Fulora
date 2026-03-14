using System.Text.Json;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

// ==================== Test middleware implementations ====================

public sealed class LoggingMiddleware : IBridgeMiddleware
{
    public List<string> Calls { get; } = new();

    public async Task<object?> InvokeAsync(BridgeCallContext context, BridgeCallHandler pipeline)
    {
        Calls.Add($"{context.ServiceName}.{context.MethodName}");
        return await pipeline(context);
    }
}

public sealed class ShortCircuitMiddleware : IBridgeMiddleware
{
    public Task<object?> InvokeAsync(BridgeCallContext context, BridgeCallHandler pipeline)
    {
        return Task.FromResult<object?>("short-circuited");
    }
}

public sealed class ErrorTransformMiddleware : IBridgeMiddleware
{
    public async Task<object?> InvokeAsync(BridgeCallContext context, BridgeCallHandler pipeline)
    {
        try
        {
            return await pipeline(context);
        }
        catch (Exception)
        {
            throw new WebViewRpcException(-32099, "Transformed error");
        }
    }
}

// ==================== Tests ====================

public sealed class BridgeMiddlewareTests
{
    private readonly TestDispatcher _dispatcher = new();

    private (WebViewCore Core, MockWebViewAdapter Adapter, List<string> Scripts) CreateCore()
    {
        var adapter = MockWebViewAdapter.Create();
        var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });
        var scripts = new List<string>();
        adapter.ScriptCallback = script => { scripts.Add(script); return null; };
        return (core, adapter, scripts);
    }

    // ==================== Middleware pipeline basic ====================

    [Fact]
    public void Middleware_receives_call_context()
    {
        var (core, adapter, scripts) = CreateCore();
        var logger = new LoggingMiddleware();

        core.Bridge.Expose<IAppService>(new FakeAppService(), new BridgeOptions
        {
            Middleware = [logger]
        });
        _dispatcher.RunAll();
        scripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"mw-1","method":"AppService.getCurrentUser","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();

        Assert.Single(logger.Calls);
        Assert.Equal("AppService.getCurrentUser", logger.Calls[0]);
        Assert.True(scripts.Any(s => s.Contains("Alice")), "Expected normal response");
    }

    [Fact]
    public void Multiple_middlewares_execute_in_order()
    {
        var (core, adapter, scripts) = CreateCore();
        var log1 = new LoggingMiddleware();
        var log2 = new LoggingMiddleware();

        core.Bridge.Expose<IAppService>(new FakeAppService(), new BridgeOptions
        {
            Middleware = [log1, log2]
        });
        _dispatcher.RunAll();
        scripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"mw-2","method":"AppService.getCurrentUser","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();

        Assert.Single(log1.Calls);
        Assert.Single(log2.Calls);
    }

    // ==================== Short-circuit ====================

    [Fact]
    public void Middleware_can_short_circuit_pipeline()
    {
        var (core, adapter, scripts) = CreateCore();

        core.Bridge.Expose<IAppService>(new FakeAppService(), new BridgeOptions
        {
            Middleware = [new ShortCircuitMiddleware()]
        });
        _dispatcher.RunAll();
        scripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"sc-1","method":"AppService.getCurrentUser","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();

        Assert.True(scripts.Any(s => s.Contains("short-circuited")),
            "Expected short-circuited response");
        Assert.DoesNotContain(scripts, s => s.Contains("Alice"));
    }

    // ==================== RateLimit as middleware ====================

    [Fact]
    public void RateLimit_via_BridgeOptions_still_works()
    {
        var (core, adapter, scripts) = CreateCore();

        core.Bridge.Expose<IAppService>(new FakeAppService(), new BridgeOptions
        {
            RateLimit = new RateLimit(1, TimeSpan.FromSeconds(10))
        });
        _dispatcher.RunAll();
        scripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"rl-1","method":"AppService.getCurrentUser","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
        Assert.True(scripts.Any(s => s.Contains("Alice")), "First call should succeed");

        scripts.Clear();
        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"rl-2","method":"AppService.getCurrentUser","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
        Assert.True(scripts.Any(s => s.Contains("-32029")), "Second call should be rate-limited");
    }

    [Fact]
    public void RateLimit_and_custom_middleware_compose()
    {
        var (core, adapter, scripts) = CreateCore();
        var logger = new LoggingMiddleware();

        core.Bridge.Expose<IAppService>(new FakeAppService(), new BridgeOptions
        {
            RateLimit = new RateLimit(10, TimeSpan.FromSeconds(10)),
            Middleware = [logger]
        });
        _dispatcher.RunAll();
        scripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"rlm-1","method":"AppService.getCurrentUser","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();

        Assert.Single(logger.Calls);
        Assert.True(scripts.Any(s => s.Contains("Alice")), "Call should succeed");
    }

    // ==================== Error transform ====================

    [Fact]
    public void Middleware_can_transform_errors()
    {
        var (core, adapter, scripts) = CreateCore();

        core.Bridge.Expose<IAppService>(new ThrowingAppService(), new BridgeOptions
        {
            Middleware = [new ErrorTransformMiddleware()]
        });
        _dispatcher.RunAll();
        scripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"et-1","method":"AppService.getCurrentUser","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();

        Assert.True(scripts.Any(s => s.Contains("Transformed error")),
            "Expected transformed error message");
    }

    // ==================== No middleware (null) works normally ====================

    [Fact]
    public void Null_middleware_works_normally()
    {
        var (core, adapter, scripts) = CreateCore();

        core.Bridge.Expose<IAppService>(new FakeAppService(), new BridgeOptions
        {
            Middleware = null
        });
        _dispatcher.RunAll();
        scripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"nm-1","method":"AppService.getCurrentUser","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();

        Assert.True(scripts.Any(s => s.Contains("Alice")), "Expected normal response");
    }

    // ==================== BridgeCallContext properties bag ====================

    [Fact]
    public void Middleware_can_pass_data_via_Properties()
    {
        var (core, adapter, scripts) = CreateCore();
        var setter = new PropertySetterMiddleware();
        var reader = new PropertyReaderMiddleware();

        core.Bridge.Expose<IAppService>(new FakeAppService(), new BridgeOptions
        {
            Middleware = [setter, reader]
        });
        _dispatcher.RunAll();
        scripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"pp-1","method":"AppService.getCurrentUser","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();

        Assert.True(reader.SawFlag, "Reader middleware should have seen the flag set by setter");
    }
}

internal sealed class PropertySetterMiddleware : IBridgeMiddleware
{
    public Task<object?> InvokeAsync(BridgeCallContext context, BridgeCallHandler pipeline)
    {
        context.Properties["test-flag"] = true;
        return pipeline(context);
    }
}

internal sealed class PropertyReaderMiddleware : IBridgeMiddleware
{
    public bool SawFlag { get; private set; }

    public Task<object?> InvokeAsync(BridgeCallContext context, BridgeCallHandler pipeline)
    {
        SawFlag = context.Properties.TryGetValue("test-flag", out var v) && v is true;
        return pipeline(context);
    }
}
