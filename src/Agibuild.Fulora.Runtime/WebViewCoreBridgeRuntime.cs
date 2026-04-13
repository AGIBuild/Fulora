using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal interface IWebViewCoreBridgeHost
{
    bool IsDisposed { get; }

    Guid ChannelId { get; }

    Task<string?> InvokeScriptAsync(string script);

    void ObserveBackgroundTask(Task task, string operationType);

    void RaiseWebMessageReceived(WebMessageReceivedEventArgs args);

    void ThrowIfDisposed();

    void ThrowIfNotOnUiThread(string apiName);
}

internal sealed class WebViewCoreBridgeRuntime : IDisposable, IWebViewCoreBridgeOperations
{
    private readonly IWebViewCoreBridgeHost _host;
    private readonly ILogger _logger;
    private readonly bool _enableDevToolsByDefault;

    private bool _webMessageBridgeEnabled;
    private IWebMessagePolicy? _webMessagePolicy;
    private IWebMessageDropDiagnosticsSink? _webMessageDropDiagnosticsSink;
    private IFuloraDiagnosticsSink? _fuloraDiagnosticsSink;
    private WebViewRpcService? _rpcService;
    private RuntimeBridgeService? _bridgeService;
    private IBridgeTracer? _bridgeTracer;

    public WebViewCoreBridgeRuntime(
        IWebViewCoreBridgeHost host,
        ILogger logger,
        bool enableDevToolsByDefault)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                _logger.LogWarning("BridgeTracer set after Bridge was already created — change ignored.");
                return;
            }

            _bridgeTracer = value;
        }
    }

    public IBridgeService Bridge
    {
        get
        {
            _host.ThrowIfDisposed();

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
                script => _host.InvokeScriptAsync(script),
                _logger,
                enableDevTools: _enableDevToolsByDefault,
                tracer: _bridgeTracer);

            _logger.LogDebug("Bridge: auto-created RuntimeBridgeService");
            return _bridgeService;
        }
    }

    public void EnableWebMessageBridge(WebMessageBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _host.ThrowIfDisposed();
        _host.ThrowIfNotOnUiThread(nameof(EnableWebMessageBridge));

        _webMessageBridgeEnabled = true;
        _webMessagePolicy = new DefaultWebMessagePolicy(options.AllowedOrigins, options.ProtocolVersion, _host.ChannelId);
        _webMessageDropDiagnosticsSink = options.DropDiagnosticsSink;
        _fuloraDiagnosticsSink = options.DiagnosticsSink;
        _rpcService ??= new WebViewRpcService(script => _host.InvokeScriptAsync(script), _logger, options.EnableDevToolsDiagnostics);

        _host.ObserveBackgroundTask(
            _host.InvokeScriptAsync(WebViewRpcService.JsStub),
            $"{nameof(EnableWebMessageBridge)}.{nameof(WebViewRpcService.JsStub)}");

        _logger.LogDebug(
            "WebMessageBridge enabled: originCount={Count}, protocol={Protocol}",
            options.AllowedOrigins?.Count ?? 0,
            options.ProtocolVersion);
    }

    public void DisableWebMessageBridge()
    {
        _host.ThrowIfDisposed();
        _host.ThrowIfNotOnUiThread(nameof(DisableWebMessageBridge));

        _webMessageBridgeEnabled = false;
        _webMessagePolicy = null;
        _webMessageDropDiagnosticsSink = null;
        _fuloraDiagnosticsSink = null;
        _rpcService = null;

        _logger.LogDebug("WebMessageBridge disabled");
    }

    public void ReinjectBridgeStubsIfEnabled()
    {
        if (!_webMessageBridgeEnabled)
        {
            return;
        }

        _host.ObserveBackgroundTask(_host.InvokeScriptAsync(WebViewRpcService.JsStub), "ReinjectBridgeStubs.RpcStub");
        _bridgeService?.ReinjectServiceStubs();

        _logger.LogDebug("Bridge: re-injected JS stubs after navigation");
    }

    public void HandleAdapterWebMessageReceivedOnUiThread(WebMessageReceivedEventArgs args)
    {
        if (_host.IsDisposed)
        {
            return;
        }

        if (!_webMessageBridgeEnabled)
        {
            _logger.LogDebug("WebMessageReceived: bridge not enabled, dropping");
            return;
        }

        var policy = _webMessagePolicy;
        if (policy is null)
        {
            _logger.LogDebug("WebMessageReceived: no policy, dropping");
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
                _logger.LogDebug("WebMessageReceived: handled as RPC message");
                return;
            }

            _logger.LogDebug("WebMessageReceived: policy allowed, forwarding");
            _host.RaiseWebMessageReceived(args);
            return;
        }

        var reason = decision.DropReason ?? WebMessageDropReason.OriginNotAllowed;
        _logger.LogDebug("WebMessageReceived: policy denied, reason={Reason}", reason);
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

    public void Dispose()
    {
        _bridgeService?.Dispose();
        _bridgeService = null;
    }
}
