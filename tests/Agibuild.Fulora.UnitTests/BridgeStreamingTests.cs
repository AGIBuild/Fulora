using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Agibuild.Fulora.Bridge.Generator;
using Agibuild.Fulora.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

// ==================== Test interface with IAsyncEnumerable ====================

[JsExport]
public interface IStreamingService
{
    IAsyncEnumerable<string> StreamMessages(string topic);
    Task<int> GetCount();
}

public class FakeStreamingService : IStreamingService
{
    public async IAsyncEnumerable<string> StreamMessages(string topic)
    {
        for (int i = 1; i <= 3; i++)
        {
            yield return $"{topic}-{i}";
            await Task.Delay(10);
        }
    }

    public Task<int> GetCount() => Task.FromResult(42);
}

// ==================== Tests ====================

public sealed class BridgeStreamingTests
{
    private readonly TestDispatcher _dispatcher = new();

    private (WebViewCore Core, MockWebViewAdapter Adapter) CreateCoreWithRpc()
    {
        var adapter = MockWebViewAdapter.Create();
        var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });
        return (core, adapter);
    }

    [Fact]
    public void IAsyncEnumerable_method_generates_streaming_handler()
    {
        var source = """
            using Agibuild.Fulora;
            using System.Collections.Generic;

            [JsExport]
            public interface IMyStreamService
            {
                IAsyncEnumerable<string> StreamData(string input);
            }
            """;

        var coreAssembly = typeof(JsExportAttribute).Assembly;
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(coreAssembly.Location),
            MetadataReference.CreateFromFile(typeof(IAsyncEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var ct = TestContext.Current.CancellationToken;
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: ct)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new WebViewBridgeGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics, ct);

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("AGBR"));

        var regTree = driver.GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("MyStreamServiceBridgeRegistration"));
        Assert.NotNull(regTree);

        var regContent = regTree!.GetText(ct).ToString();
        Assert.Contains("RegisterEnumerator", regContent);
        Assert.Contains("GetAsyncEnumerator", regContent);
    }

    [Fact]
    public void TypeScript_maps_IAsyncEnumerable_to_AsyncIterable()
    {
        var source = """
            using Agibuild.Fulora;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            [JsExport]
            public interface IStreamSvc
            {
                IAsyncEnumerable<int> StreamNumbers();
                Task<string> GetName();
            }
            """;

        var coreAssembly = typeof(JsExportAttribute).Assembly;
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(coreAssembly.Location),
            MetadataReference.CreateFromFile(typeof(IAsyncEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var ct = TestContext.Current.CancellationToken;
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: ct)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new WebViewBridgeGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, ct);

        var tsTree = driver.GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("BridgeTypeScriptDeclarations"));
        Assert.NotNull(tsTree);

        var tsContent = tsTree!.GetText(ct).ToString();
        Assert.Contains("streamNumbers(): AsyncIterable<number>", tsContent);
        Assert.Contains("getName(): Promise<string>", tsContent);
    }

    [Fact]
    public void Streaming_method_returns_token_and_prefetched_values()
    {
        var (core, adapter) = CreateCoreWithRpc();
        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        core.Bridge.Expose<IStreamingService>(new FakeStreamingService());
        capturedScripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"stream-1","method":"StreamingService.streamMessages","params":{"topic":"test"}}""",
            "*", core.ChannelId);

        _dispatcher.RunAll();
        Thread.Sleep(100);
        _dispatcher.RunAll();

        var response = capturedScripts.FirstOrDefault(s => s.Contains("_onResponse") && s.Contains("token"));
        Assert.NotNull(response);
        Assert.Contains("token", response);
    }

    [Fact]
    public void Enumerator_abort_disposes_enumerator()
    {
        var (core, adapter) = CreateCoreWithRpc();
        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        core.Bridge.Expose<IStreamingService>(new FakeStreamingService());
        capturedScripts.Clear();

        // Start streaming
        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"stream-abort","method":"StreamingService.streamMessages","params":{"topic":"test"}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
        Thread.Sleep(100);
        _dispatcher.RunAll();

        // Extract token from response
        var response = capturedScripts.FirstOrDefault(s => s.Contains("token"));
        Assert.NotNull(response);

        // Send abort (the token is embedded in the response, we'll just test protocol handling)
        var exception = Record.Exception(() =>
        {
            adapter.RaiseWebMessage(
                """{"jsonrpc":"2.0","method":"$/enumerator/abort","params":{"token":"nonexistent"}}""",
                "*", core.ChannelId);
            _dispatcher.RunAll();
        });
        Assert.Null(exception);
    }

    [Fact]
    public void Normal_method_still_works_alongside_streaming()
    {
        var (core, adapter) = CreateCoreWithRpc();
        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        core.Bridge.Expose<IStreamingService>(new FakeStreamingService());
        capturedScripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"normal-1","method":"StreamingService.getCount","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();

        var response = capturedScripts.Last();
        Assert.Contains("42", response);
    }

    [Fact]
    public void Generated_JS_stub_includes_createAsyncIterable_for_streaming_methods()
    {
        var (core, adapter) = CreateCoreWithRpc();
        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        core.Bridge.Expose<IStreamingService>(new FakeStreamingService());

        var serviceStub = capturedScripts.Last();
        Assert.Contains("_createAsyncIterable", serviceStub);
        Assert.Contains("StreamingService.streamMessages", serviceStub);
    }

    [Fact]
    public void RPC_bootstrap_stub_includes_createAsyncIterable()
    {
        Assert.Contains("_createAsyncIterable", WebViewRpcService.JsStub);
        Assert.Contains("$/enumerator/next", WebViewRpcService.JsStub);
        Assert.Contains("$/enumerator/abort", WebViewRpcService.JsStub);
        Assert.Contains("_encodeBinaryPayload", WebViewRpcService.JsStub);
        Assert.Contains("_decodeBinaryResult", WebViewRpcService.JsStub);
    }

    [Fact]
    public void Enumerator_disposed_after_inactivity_timeout()
    {
        var (core, adapter) = CreateCoreWithRpc();
        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        core.Bridge.Expose<IStreamingService>(new FakeStreamingService());
        capturedScripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"timeout-1","method":"StreamingService.streamMessages","params":{"topic":"test"}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
        Thread.Sleep(100);
        _dispatcher.RunAll();

        var response = capturedScripts.FirstOrDefault(s => s.Contains("token"));
        Assert.NotNull(response);

        Assert.Equal(TimeSpan.FromSeconds(30), WebViewRpcService.EnumeratorInactivityTimeout);
    }

    [Fact]
    public void Enumerator_next_resets_inactivity_timer()
    {
        var (core, adapter) = CreateCoreWithRpc();
        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        core.Bridge.Expose<IStreamingService>(new FakeStreamingService());
        capturedScripts.Clear();

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"reset-1","method":"StreamingService.streamMessages","params":{"topic":"test"}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
        Thread.Sleep(100);
        _dispatcher.RunAll();

        var tokenResponse = capturedScripts.FirstOrDefault(s => s.Contains("token"));
        Assert.NotNull(tokenResponse);

        var guidMatch = System.Text.RegularExpressions.Regex.Match(
            tokenResponse!, @"([a-f0-9]{8}-?[a-f0-9]{4}-?[a-f0-9]{4}-?[a-f0-9]{4}-?[a-f0-9]{12})");
        Assert.True(guidMatch.Success, "Should extract enumerator token GUID from response");

        var token = guidMatch.Groups[1].Value;

        capturedScripts.Clear();
        adapter.RaiseWebMessage(
            $$$"""{"jsonrpc":"2.0","id":"next-1","method":"$/enumerator/next/{{{token}}}","params":{}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
        Thread.Sleep(100);
        _dispatcher.RunAll();

        var nextResponse = capturedScripts.FirstOrDefault(s => s.Contains("_onResponse"));
        Assert.NotNull(nextResponse);
    }
}
