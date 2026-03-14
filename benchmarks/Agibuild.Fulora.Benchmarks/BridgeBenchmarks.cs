using System.Runtime.CompilerServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Agibuild.Fulora.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class BridgeBenchmarks : IDisposable
{
    private WebViewCore _core = null!;
    private Testing.MockWebViewAdapter _adapter = null!;
    private Testing.TestDispatcher _dispatcher = null!;

    [JsExport]
    public interface ICalcService
    {
        Task<int> Add(int a, int b);
    }

    [JsExport]
    public interface ICancellableBenchService
    {
        Task<string> Process(string input, CancellationToken ct);
    }

    [JsExport]
    public interface IStreamBenchService
    {
        IAsyncEnumerable<int> StreamNumbers(int count);
    }

    private sealed class CalcServiceImpl : ICalcService
    {
        public Task<int> Add(int a, int b) => Task.FromResult(a + b);
    }

    private sealed class CancellableBenchServiceImpl : ICancellableBenchService
    {
        public Task<string> Process(string input, CancellationToken ct) => Task.FromResult(input);
    }

    private sealed class StreamBenchServiceImpl : IStreamBenchService
    {
        public async IAsyncEnumerable<int> StreamNumbers(int count)
        {
            for (var i = 0; i < count; i++)
                yield return i;
            await Task.CompletedTask;
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        _dispatcher = new Testing.TestDispatcher();
        _adapter = new Testing.MockWebViewAdapter();
        _core = new WebViewCore(_adapter, _dispatcher);

        _core.EnableWebMessageBridge(new WebMessageBridgeOptions());
        _dispatcher.RunAll();

        _core.Bridge.Expose<ICalcService>(new CalcServiceImpl());
        _core.Bridge.Expose<ICancellableBenchService>(new CancellableBenchServiceImpl());
        _core.Bridge.Expose<IStreamBenchService>(new StreamBenchServiceImpl());
        _dispatcher.RunAll();
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        _core?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Description = "Bridge: JS→C# typed call (Add)")]
    public async Task BridgeTypedCall()
    {
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "CalcService.Add",
            @params = new { a = 3, b = 4 }
        });

        _adapter.RaiseWebMessage(request, "app://localhost", Guid.Empty);
        _dispatcher.RunAll();
        await Task.CompletedTask;
    }

    [Benchmark(Description = "Bridge: Expose + Remove cycle")]
    public void BridgeExposeRemoveCycle()
    {
        _core.Bridge.Expose<ICalcService>(new CalcServiceImpl());
        _dispatcher.RunAll();
        _core.Bridge.Remove<ICalcService>();
    }

    [Benchmark(Description = "Bridge: CancellationToken dispatch")]
    public async Task BridgeCancellationDispatch()
    {
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "CancellableBenchService.Process",
            @params = new { input = "bench" }
        });

        _adapter.RaiseWebMessage(request, "app://localhost", Guid.Empty);
        _dispatcher.RunAll();
        await Task.CompletedTask;
    }

    [Benchmark(Description = "Bridge: IAsyncEnumerable streaming")]
    public async Task BridgeStreamingDispatch()
    {
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "StreamBenchService.StreamNumbers",
            @params = new { count = 5 }
        });

        _adapter.RaiseWebMessage(request, "app://localhost", Guid.Empty);
        _dispatcher.RunAll();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Measures raw RPC (non-typed) overhead for comparison.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class RpcBenchmarks : IDisposable
{
    private WebViewCore _core = null!;
    private Testing.MockWebViewAdapter _adapter = null!;
    private Testing.TestDispatcher _dispatcher = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dispatcher = new Testing.TestDispatcher();
        _adapter = new Testing.MockWebViewAdapter();
        _core = new WebViewCore(_adapter, _dispatcher);

        _core.EnableWebMessageBridge(new WebMessageBridgeOptions());
        _dispatcher.RunAll();

        // Register raw RPC handler
        _core.Rpc!.Handle("echo", (JsonElement? args) =>
        {
            return Task.FromResult<object?>(args);
        });
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        _core?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Description = "Raw RPC: JS→C# echo")]
    public void RawRpcEcho()
    {
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "echo",
            @params = new { message = "hello" }
        });

        _adapter.RaiseWebMessage(request, "app://localhost", Guid.Empty);
        _dispatcher.RunAll();
    }
}
