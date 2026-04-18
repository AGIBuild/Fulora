using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal sealed class WebViewCoreBridgeRuntime : IDisposable
{
    private readonly WebViewCoreContext _context;
    private readonly bool _enableDevToolsByDefault;

    private bool _webMessageBridgeEnabled;
    private IWebMessagePolicy? _webMessagePolicy;
    private IWebMessageDropDiagnosticsSink? _webMessageDropDiagnosticsSink;
    private IFuloraDiagnosticsSink? _fuloraDiagnosticsSink;
    private WebViewRpcService? _rpcService;
    private RuntimeBridgeService? _bridgeService;
    private IBridgeTracer? _bridgeTracer;

    public WebViewCoreBridgeRuntime(
        WebViewCoreContext context,
        bool enableDevToolsByDefault)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _enableDevToolsByDefault = enableDevToolsByDefault;
    }

    public bool IsBridgeEnabled => _webMessageBridgeEnabled;

    public IWebViewRpcService? Rpc => _rpcService;

    public IBridgeTracer? BridgeTracer
    {
        get => _bridgeTracer;
        set
        {
            if (_bridgeService is not null)
            {
                _context.Logger.LogWarning("BridgeTracer set after Bridge was already created — change ignored.");
                return;
            }

            _bridgeTracer = value;
        }
    }

    public IBridgeService Bridge
    {
        get
        {
            _context.ThrowIfDisposed();

            if (_bridgeService is not null)
            {
                return _bridgeService;
            }

            if (!_webMessageBridgeEnabled)
            {
                EnableWebMessageBridge(new WebMessageBridgeOptions
                {
                    EnableDevToolsDiagnostics = _enableDevToolsByDefault
                });
            }

            _bridgeService = new RuntimeBridgeService(
                _rpcService!,
                script => InvokeScriptAsync(script),
                _context.Logger,
                enableDevTools: _enableDevToolsByDefault,
                tracer: _bridgeTracer);

            _context.Logger.LogDebug("Bridge: auto-created RuntimeBridgeService");
            return _bridgeService;
        }
    }

    public void EnableWebMessageBridge(WebMessageBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _context.ThrowIfDisposed();
        _context.ThrowIfNotOnUiThread(nameof(EnableWebMessageBridge));

        _webMessageBridgeEnabled = true;
        _webMessagePolicy = new DefaultWebMessagePolicy(options.AllowedOrigins, options.ProtocolVersion, _context.ChannelId);
        _webMessageDropDiagnosticsSink = options.DropDiagnosticsSink;
        _fuloraDiagnosticsSink = options.DiagnosticsSink;
        _rpcService ??= new WebViewRpcService(script => InvokeScriptAsync(script), _context.Logger, options.EnableDevToolsDiagnostics);

        _context.ObserveBackgroundTask(
            InvokeScriptAsync(WebViewRpcService.JsStub),
            $"{nameof(EnableWebMessageBridge)}.{nameof(WebViewRpcService.JsStub)}");

        _context.Logger.LogDebug(
            "WebMessageBridge enabled: originCount={Count}, protocol={Protocol}",
            options.AllowedOrigins?.Count ?? 0,
            options.ProtocolVersion);
    }

    public void DisableWebMessageBridge()
    {
        _context.ThrowIfDisposed();
        _context.ThrowIfNotOnUiThread(nameof(DisableWebMessageBridge));

        _webMessageBridgeEnabled = false;
        _webMessagePolicy = null;
        _webMessageDropDiagnosticsSink = null;
        _fuloraDiagnosticsSink = null;
        _rpcService = null;

        _context.Logger.LogDebug("WebMessageBridge disabled");
    }

    public void ReinjectBridgeStubsIfEnabled()
    {
        if (!_webMessageBridgeEnabled)
        {
            return;
        }

        _context.ObserveBackgroundTask(InvokeScriptAsync(WebViewRpcService.JsStub), "ReinjectBridgeStubs.RpcStub");
        _bridgeService?.ReinjectServiceStubs();

        _context.Logger.LogDebug("Bridge: re-injected JS stubs after navigation");
    }

    /// <summary>
    /// Adapter-thread entry point: logs + dispatches to UI thread, filtering disposed / destroyed hosts.
    /// Mirrors the pattern used by <see cref="WebViewCoreAdapterEventRuntime"/> so all adapter events
    /// go through <see cref="UiThreadHelper.SafeDispatch"/> at the runtime layer rather than scattered
    /// across <see cref="WebViewCore"/>.
    /// </summary>
    public void HandleAdapterWebMessageReceived(WebMessageReceivedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _context.Logger.LogDebug("Event WebMessageReceived: origin={Origin}, channelId={ChannelId}", args.Origin, args.ChannelId);

        UiThreadHelper.SafeDispatch(
            _context.Dispatcher,
            _context.Lifecycle.IsDisposed,
            _context.Lifecycle.IsAdapterDestroyed,
            () => HandleAdapterWebMessageReceivedOnUiThread(args),
            _context.Logger,
            "WebMessageReceived: ignored (disposed or destroyed)");
    }

    internal void HandleAdapterWebMessageReceivedOnUiThread(WebMessageReceivedEventArgs args)
    {
        if (_context.Lifecycle.IsDisposed)
        {
            return;
        }

        if (!_webMessageBridgeEnabled)
        {
            _context.Logger.LogDebug("WebMessageReceived: bridge not enabled, dropping");
            return;
        }

        var policy = _webMessagePolicy;
        if (policy is null)
        {
            _context.Logger.LogDebug("WebMessageReceived: no policy, dropping");
            return;
        }

        var envelope = new WebMessageEnvelope(
            Body: args.Body,
            Origin: args.Origin,
            ChannelId: args.ChannelId,
            ProtocolVersion: args.ProtocolVersion);

        var decision = policy.Evaluate(in envelope);
        if (decision.IsAllowed)
        {
            if (_rpcService is not null && _rpcService.TryProcessMessage(args.Body))
            {
                _context.Logger.LogDebug("WebMessageReceived: handled as RPC message");
                return;
            }

            _context.Logger.LogDebug("WebMessageReceived: policy allowed, forwarding");
            _context.Events.RaiseWebMessageReceived(args);
            return;
        }

        var reason = decision.DropReason ?? WebMessageDropReason.OriginNotAllowed;
        _context.Logger.LogDebug("WebMessageReceived: policy denied, reason={Reason}", reason);
        _webMessageDropDiagnosticsSink?.OnMessageDropped(new WebMessageDropDiagnostic(reason, args.Origin, args.ChannelId));
        _fuloraDiagnosticsSink?.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "runtime.webmessage.dropped",
            Layer = "runtime",
            Component = nameof(WebViewCore),
            ChannelId = args.ChannelId.ToString("D"),
            Status = "dropped",
            ErrorType = reason.ToString(),
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = args.Origin,
                ["dropReason"] = reason.ToString()
            }
        });
    }

    /// <summary>
    /// Invokes a script through the public async pipeline so the call is serialized, dispatched onto
    /// the UI thread, and classified for failure reporting. Kept private to the bridge runtime — the
    /// public entry point remains <see cref="WebViewCore.InvokeScriptAsync"/>.
    /// </summary>
    private Task<string?> InvokeScriptAsync(string script)
        => _context.Operations.EnqueueAsync<string?>(
            "InvokeScriptAsync",
            () => _context.Adapter.InvokeScriptAsync(script));

    public void Dispose()
    {
        _bridgeService?.Dispose();
        _bridgeService = null;
    }
}
