using Agibuild.Fulora;

namespace Agibuild.Fulora.Testing;

/// <summary>
/// Test app fixture for hybrid E2E testing. Creates a WebViewCore with mock adapter
/// and bridge tracer for test assertions.
/// </summary>
public sealed class FuloraTestApp : IAsyncDisposable
{
    private WebViewCore? _core;
    private readonly BridgeTestTracer _tracer = new();
    private readonly TestDispatcher _dispatcher = new();

    private FuloraTestApp()
    {
    }

    /// <summary>
    /// Creates a configured test app with WebViewCore, MockWebViewAdapter, TestDispatcher, and BridgeTestTracer.
    /// </summary>
    public static FuloraTestApp Create()
    {
        var app = new FuloraTestApp();
        var adapter = MockWebViewAdapter.Create();
        adapter.AutoCompleteNavigation = true;

        var core = new WebViewCore(adapter, app._dispatcher);
        core.BridgeTracer = app._tracer;
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });

        app._core = core;
        return app;
    }

    /// <summary>
    /// Gets a handle for interacting with the WebView.
    /// </summary>
    public WebViewTestHandle GetWebView()
    {
        if (_core is null)
        {
            throw new ObjectDisposedException(nameof(FuloraTestApp));
        }
        return new WebViewTestHandle(_core, _tracer);
    }

    /// <summary>
    /// The bridge tracer for observing and asserting on bridge calls.
    /// </summary>
    public BridgeTestTracer Tracer => _tracer;

    /// <summary>
    /// The test dispatcher for pumping UI work in tests.
    /// </summary>
    public TestDispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// The underlying WebViewCore (for advanced scenarios).
    /// </summary>
    public WebViewCore? Core => _core;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_core is not null)
        {
            _core.Dispose();
            _core = null;
        }
        return ValueTask.CompletedTask;
    }
}
