using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Platforms.Macios;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.AppKit;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;
using Agibuild.Fulora.Security;

namespace Agibuild.Fulora.Adapters.MacOS;

[SupportedOSPlatform("macos")]
internal sealed class MacOSWebViewAdapter : IWebViewAdapter
{
    private readonly INavigationSecurityHooks _securityHooks;
    private readonly object _navLock = new();
    private readonly Dictionary<string, string> _preloadScripts = new();
    private readonly ConcurrentQueue<string?> _pendingDownloadPaths = new();

    private IWebViewAdapterHost? _host;
    private bool _initialized;
    private bool _attached;
    private bool _detached;

    private WKWebViewConfiguration? _configuration;
    private WKWebsiteDataStore? _dataStore;
    private WKUserContentController? _userContentController;
    private WKWebView? _webView;
    private WKNavigationDelegate? _navigationDelegate;
    private WKUIDelegate? _uiDelegate;
    private WKScriptMessageHandler? _scriptMessageHandler;
    private WKURLSchemeHandlerImpl? _schemeHandler;
    private WKDownloadDelegate? _downloadDelegate;
    private NSView? _parentView;
    private NSView? _webViewAsView;

    private IReadOnlyList<CustomSchemeRegistration>? _customSchemes;
    private bool _enableDevTools;
    private bool _useEphemeralSession;
    private bool _transparentBackground;
    private string? _customUserAgent;

    private Guid _activeNavigationId;
    private Uri? _activeRequestUri;
    private bool _activeNavigationCompleted;
    private bool _apiNavigationActive;
    private bool _nativeCorrelationActive;
    private Guid _nativeCorrelationId;
    private double _zoomFactor = 1.0;

    public MacOSWebViewAdapter()
        : this(DefaultNavigationSecurityHooks.Instance)
    {
    }

    internal MacOSWebViewAdapter(INavigationSecurityHooks securityHooks)
    {
        _securityHooks = securityHooks;
    }

    public bool CanGoBack => _attached && !_detached && _webView?.CanGoBack == true;

    public bool CanGoForward => _attached && !_detached && _webView?.CanGoForward == true;

    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;

    [Experimental("AGWV005")]
    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested
    {
        add { }
        remove { }
    }

    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested
    {
        add { }
        remove { }
    }

    public event EventHandler<double>? ZoomFactorChanged;

    public double ZoomFactor
    {
        get => _webView?.PageZoom ?? _zoomFactor;
        set
        {
            _zoomFactor = value;
            if (_webView is not null && !_detached)
            {
                _webView.PageZoom = value;
            }

            ZoomFactorChanged?.Invoke(this, value);
        }
    }

    public bool IsDevToolsOpen => false;

    public void Initialize(IWebViewAdapterHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_initialized)
        {
            throw new InvalidOperationException($"{nameof(Initialize)} can only be called once.");
        }

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("WKWebView adapter can only be used on macOS.");
        }

        _initialized = true;
        _host = host;
    }

    public void Attach(INativeHandle parentHandle)
    {
        ArgumentNullException.ThrowIfNull(parentHandle);
        ThrowIfNotInitialized();
        if (_detached)
        {
            throw new InvalidOperationException($"{nameof(Attach)} cannot be called after {nameof(Detach)}.");
        }

        if (_attached)
        {
            throw new InvalidOperationException($"{nameof(Attach)} can only be called once.");
        }

        if (parentHandle.Handle == IntPtr.Zero)
        {
            throw new ArgumentException("Parent handle must be non-zero.", nameof(parentHandle));
        }

        _parentView = NSView.FromHandle(parentHandle.Handle);
        _configuration = WKWebViewConfiguration.Create();
        _userContentController = new WKUserContentController();
        _scriptMessageHandler = new WKScriptMessageHandler();
        _scriptMessageHandler.DidReceiveScriptMessage += OnScriptMessageReceived;
        using (var handlerName = NSString.Create("agibuildWebView")!)
        {
            _userContentController.AddScriptMessageHandler(_scriptMessageHandler.Handle, handlerName);
        }

        _configuration.UserContentController = _userContentController.Handle;
        _dataStore = _useEphemeralSession
            ? WKWebsiteDataStore.NonPersistentDataStore()
            : WKWebsiteDataStore.DefaultDataStore();
        _configuration.WebsiteDataStore = _dataStore.Handle;

        if (_customSchemes is not null)
        {
            _schemeHandler = new WKURLSchemeHandlerImpl();
            _schemeHandler.StartTask += OnSchemeStartTask;
            _schemeHandler.StopTask += OnSchemeStopTask;
            foreach (var scheme in _customSchemes)
            {
                if (!string.IsNullOrWhiteSpace(scheme.SchemeName))
                {
                    _configuration.SetUrlSchemeHandler(_schemeHandler.Handle, scheme.SchemeName);
                }
            }
        }

        _webView = new WKWebView(_configuration);
        _webView.CustomUserAgent = _customUserAgent;
        _webView.PageZoom = _zoomFactor;
        _webView.SetInspectable(_enableDevTools);
        if (_transparentBackground)
        {
            _webView.SetDrawsBackground(false);
            _webView.SetUnderPageBackgroundColor(NSColor.ClearColor);
        }

        _navigationDelegate = new WKNavigationDelegate();
        _navigationDelegate.DecidePolicyForNavigationAction += OnDecidePolicyForNavigationAction;
        _navigationDelegate.DecidePolicyForNavigationResponse += OnDecidePolicyForNavigationResponse;
        _navigationDelegate.DidFinishNavigation += OnDidFinishNavigation;
        _navigationDelegate.DidFailNavigation += OnDidFailNavigation;
        _navigationDelegate.DidFailProvisionalNavigation += OnDidFailNavigation;
        _navigationDelegate.DidReceiveServerTrustChallenge += OnDidReceiveServerTrustChallenge;
        _navigationDelegate.DidBecomeDownload += OnDidBecomeDownload;
        _webView.NavigationDelegate = _navigationDelegate;

        _uiDelegate = new WKUIDelegate();
        _uiDelegate.MediaCapturePermissionRequested += OnRequestMediaCapturePermission;
        _webView.UIDelegate = _uiDelegate;

        if (OperatingSystem.IsMacOSVersionAtLeast(11, 3))
        {
            _downloadDelegate = new WKDownloadDelegate();
            _downloadDelegate.DecideDestination += OnDecideDownloadDestination;
        }

        _webViewAsView = NSView.FromHandle(_webView.Handle);
        _webViewAsView.Frame = _parentView.Bounds;
        _webViewAsView.AutoresizingMask = NSViewAutoresizingMask.WidthSizable | NSViewAutoresizingMask.HeightSizable;
        _parentView.AddSubview(_webView);
        _attached = true;
    }

    public void Detach()
    {
        ThrowIfNotInitialized();
        if (_detached)
        {
            return;
        }

        _detached = true;
        _attached = false;

        try
        {
            _webViewAsView?.RemoveFromSuperview();
            if (_userContentController is not null)
            {
                using var name = NSString.Create("agibuildWebView")!;
                _userContentController.RemoveScriptMessageHandlerForName(name);
            }
        }
        finally
        {
            _downloadDelegate?.Dispose();
            _schemeHandler?.Dispose();
            _scriptMessageHandler?.Dispose();
            _uiDelegate?.Dispose();
            _navigationDelegate?.Dispose();
            _webView?.Dispose();
            _userContentController?.Dispose();
            _configuration?.Dispose();
            _dataStore?.Dispose();
            _parentView?.Dispose();
            _webViewAsView?.Dispose();
            ClearNavigationState();
        }
    }

    public Task NavigateAsync(Guid navigationId, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ThrowIfNotAttached();
        lock (_navLock)
        {
            BeginApiNavigation(navigationId, uri);
        }

        using var request = NSURLRequest.FromUri(uri);
        _webView!.Load(request);
        return Task.CompletedTask;
    }

    public Task NavigateToStringAsync(Guid navigationId, string html)
        => NavigateToStringAsync(navigationId, html, baseUrl: null);

    public Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ThrowIfNotAttached();
        lock (_navLock)
        {
            BeginApiNavigation(navigationId, baseUrl ?? new Uri("about:blank"));
        }

        using var baseNsUrl = baseUrl is null ? null : new NSUrl(NSString.Create(baseUrl.AbsoluteUri)!);
        _webView!.LoadHTMLString(html, baseNsUrl);
        return Task.CompletedTask;
    }

    public async Task<string?> InvokeScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        ThrowIfNotAttached();
        var result = await _webView!.EvaluateJavaScriptAsync(script).ConfigureAwait(false);
        return JsResultToString(result);
    }

    public bool GoBack(Guid navigationId)
    {
        ThrowIfNotAttached();
        if (!CanGoBack)
        {
            return false;
        }

        lock (_navLock)
        {
            BeginApiNavigation(navigationId, _activeRequestUri ?? new Uri("about:blank"));
        }

        _webView!.GoBack();
        return true;
    }

    public bool GoForward(Guid navigationId)
    {
        ThrowIfNotAttached();
        if (!CanGoForward)
        {
            return false;
        }

        lock (_navLock)
        {
            BeginApiNavigation(navigationId, _activeRequestUri ?? new Uri("about:blank"));
        }

        _webView!.GoForward();
        return true;
    }

    public bool Refresh(Guid navigationId)
    {
        ThrowIfNotAttached();
        lock (_navLock)
        {
            BeginApiNavigation(navigationId, _activeRequestUri ?? new Uri("about:blank"));
        }

        _webView!.Reload();
        return true;
    }

    public bool Stop()
    {
        ThrowIfNotAttached();
        _webView!.Stop();
        return true;
    }

    public INativeHandle? TryGetWebViewHandle()
    {
        if (!_attached || _detached || _webView is null)
        {
            return null;
        }

        return new AppleWKWebViewPlatformHandle(_webView.Handle);
    }

    public void ApplyEnvironmentOptions(IWebViewEnvironmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfNotInitialized();
        if (_attached)
        {
            throw new InvalidOperationException("Environment options must be applied before Attach.");
        }

        _enableDevTools = options.EnableDevTools;
        _customUserAgent = options.CustomUserAgent;
        _useEphemeralSession = options.UseEphemeralSession;
        _transparentBackground = options.TransparentBackground;
    }

    public void SetCustomUserAgent(string? userAgent)
    {
        ThrowIfNotInitialized();
        _customUserAgent = userAgent;
        if (_webView is not null && !_detached)
        {
            _webView.CustomUserAgent = userAgent;
        }
    }

    public void RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes)
    {
        _customSchemes = schemes;
        if (_attached)
        {
            throw new InvalidOperationException("Custom schemes must be registered before Attach.");
        }
    }

    public async Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ThrowIfNotAttachedForCookies();
        var cookies = await CookieStore.GetAllCookiesAsync().ConfigureAwait(false);
        try
        {
            return cookies
                .Select(c => c.ToWebViewCookie())
                .Where(c => c.Domain.EndsWith(uri.Host, StringComparison.OrdinalIgnoreCase) ||
                            uri.Host.EndsWith(c.Domain, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        finally
        {
            foreach (var cookie in cookies)
            {
                cookie.Dispose();
            }
        }
    }

    public async Task SetCookieAsync(WebViewCookie cookie)
    {
        ThrowIfNotAttachedForCookies();
        using var nativeCookie = NSHTTPCookie.From(cookie);
        await CookieStore.SetCookieAsync(nativeCookie).ConfigureAwait(false);
    }

    public async Task DeleteCookieAsync(WebViewCookie cookie)
    {
        ThrowIfNotAttachedForCookies();
        var cookies = await CookieStore.GetAllCookiesAsync().ConfigureAwait(false);
        try
        {
            var target = cookies.FirstOrDefault(c =>
            {
                var managed = c.ToWebViewCookie();
                return managed.Name == cookie.Name &&
                       managed.Domain == cookie.Domain &&
                       managed.Path == cookie.Path;
            });
            if (target is not null)
            {
                await CookieStore.DeleteCookieAsync(target).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var nativeCookie in cookies)
            {
                nativeCookie.Dispose();
            }
        }
    }

    public async Task ClearAllCookiesAsync()
    {
        ThrowIfNotAttachedForCookies();
        var cookies = await CookieStore.GetAllCookiesAsync().ConfigureAwait(false);
        try
        {
            foreach (var cookie in cookies)
            {
                await CookieStore.DeleteCookieAsync(cookie).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var cookie in cookies)
            {
                cookie.Dispose();
            }
        }
    }

    public void ExecuteCommand(WebViewCommand command)
    {
        if (_webView is null || _detached)
        {
            return;
        }

        var jsCommand = command switch
        {
            WebViewCommand.Copy => "document.execCommand('copy')",
            WebViewCommand.Cut => "document.execCommand('cut')",
            WebViewCommand.Paste => "document.execCommand('paste')",
            WebViewCommand.SelectAll => "document.execCommand('selectAll')",
            WebViewCommand.Undo => "document.execCommand('undo')",
            WebViewCommand.Redo => "document.execCommand('redo')",
            _ => null
        };
        if (jsCommand is not null)
        {
            _ = _webView.EvaluateJavaScriptAsync(jsCommand);
        }
    }

    public Task<byte[]> CaptureScreenshotAsync()
    {
        ThrowIfNotAttached();
        return _webView!.CaptureScreenshotPngAsync();
    }

    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options)
    {
        ThrowIfNotAttached();
        ApplePdfPrintOptions.ThrowIfUnsupported(options);
        return _webView!.CreatePdfAsync();
    }

    public async Task<FindInPageEventArgs> FindAsync(string text, FindInPageOptions? options)
    {
        ThrowIfNotAttached();
        var literal = JsStringLiteral(text);
        var caseSensitive = (options?.CaseSensitive ?? false) ? "true" : "false";
        var forward = (options?.Forward ?? true) ? "true" : "false";
        var countScript = """
            (function(text, cs) {
              var re = new RegExp(text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), cs ? 'g' : 'gi');
              var m = document.body.innerText.match(re);
              return m ? m.length : 0;
            })
            """ + $"({literal}, {caseSensitive})";
        var totalText = await InvokeScriptAsync(countScript).ConfigureAwait(false);
        _ = int.TryParse(totalText, out var total);

        var findScript = $"window.find({literal}, {caseSensitive}, {forward}, true, false, true, false)";
        var foundText = await InvokeScriptAsync(findScript).ConfigureAwait(false);
        var found = string.Equals(foundText, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(foundText, "1", StringComparison.Ordinal);
        return new FindInPageEventArgs
        {
            ActiveMatchIndex = found ? 0 : -1,
            TotalMatches = total
        };
    }

    public void StopFind(bool clearHighlights = true)
    {
        if (_webView is null || _detached)
        {
            return;
        }

        _ = _webView.EvaluateJavaScriptAsync("window.getSelection().removeAllRanges()");
    }

    public string AddPreloadScript(string javaScript)
    {
        ArgumentNullException.ThrowIfNull(javaScript);
        ThrowIfNotAttached();
        var scriptId = $"preload_{Guid.NewGuid():N}";
        AddUserScript(javaScript);
        _preloadScripts[scriptId] = javaScript;
        return scriptId;
    }

    public void RemovePreloadScript(string scriptId)
    {
        if (_userContentController is null || _detached)
        {
            return;
        }

        if (!_preloadScripts.Remove(scriptId))
        {
            return;
        }

        _userContentController.RemoveAllUserScripts();
        foreach (var script in _preloadScripts.Values)
        {
            AddUserScript(script);
        }
    }

    public void OpenDevTools()
    {
    }

    public void CloseDevTools()
    {
    }

    private WKHTTPCookieStore CookieStore =>
        _dataStore?.HttpCookieStore ?? throw new InvalidOperationException("Adapter is not attached.");

    private void OnDecidePolicyForNavigationAction(object? sender, DecidePolicyForNavigationActionEventArgs e)
    {
        e.DeferCompletion();
        _ = CompleteNavigationActionPolicyAsync(e);
    }

    private async Task CompleteNavigationActionPolicyAsync(DecidePolicyForNavigationActionEventArgs e)
    {
        try
        {
            var allow = await DecidePolicyForNavigationActionAsync(e).ConfigureAwait(false);
            e.Complete(allow ? WKNavigationActionPolicy.Allow : WKNavigationActionPolicy.Cancel);
        }
        catch
        {
            e.Complete(WKNavigationActionPolicy.Cancel);
        }
    }

    private async Task<bool> DecidePolicyForNavigationActionAsync(DecidePolicyForNavigationActionEventArgs e)
    {
        if (_detached)
        {
            return false;
        }

        var requestUri = TryGetUri(e.Request.Url);
        if (e.IsNewWindow)
        {
            SafeRaise(() => NewWindowRequested?.Invoke(this, new NewWindowRequestedEventArgs(requestUri)));
            return false;
        }

        if (!e.IsMainFrame)
        {
            return true;
        }

        bool apiActive;
        lock (_navLock)
        {
            apiActive = _apiNavigationActive;
        }

        if (apiActive)
        {
            return true;
        }

        if (requestUri is null)
        {
            return true;
        }

        var host = _host;
        if (host is null)
        {
            return false;
        }

        Guid correlationId;
        NativeNavigationStartingInfo info;
        lock (_navLock)
        {
            var continuation = _nativeCorrelationActive && _activeNavigationId != Guid.Empty && !_activeNavigationCompleted;
            correlationId = GetOrCreateNativeCorrelationId((int)e.NavigationType, continuation);
            info = new NativeNavigationStartingInfo(correlationId, requestUri, IsMainFrame: true);
        }

        var decision = await host.OnNativeNavigationStartingAsync(info).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            if (decision.NavigationId != Guid.Empty)
            {
                CompleteCanceledFromPolicyDecision(decision.NavigationId, requestUri);
            }

            return false;
        }

        if (decision.NavigationId == Guid.Empty)
        {
            return false;
        }

        lock (_navLock)
        {
            _activeNavigationId = decision.NavigationId;
            _activeRequestUri = requestUri;
            _activeNavigationCompleted = false;
        }

        return true;
    }

    private void OnDecidePolicyForNavigationResponse(object? sender, DecidePolicyForNavigationResponseEventArgs e)
    {
        var response = e.Response;
        var mimeType = response.MimeType;
        var uri = response.Url is { } url ? TryGetUri(url) : null;
        if (uri is not null && IsLikelyDownload(mimeType))
        {
            var args = new DownloadRequestedEventArgs(
                uri,
                response.SuggestedFilename,
                mimeType,
                response.ExpectedContentLength > 0 ? response.ExpectedContentLength : null);
            SafeRaise(() => DownloadRequested?.Invoke(this, args));

            if (args.Cancel)
            {
                e.Policy = WKNavigationResponsePolicy.Cancel;
                return;
            }

            _pendingDownloadPaths.Enqueue(args.DownloadPath);
            e.Policy = WKNavigationResponsePolicy.BecomeDownload;
        }
    }

    private void OnDidBecomeDownload(object? sender, WKDownloadEventArgs e)
    {
        if (_downloadDelegate is not null)
        {
            e.Download.SetDelegate(_downloadDelegate);
        }
    }

    private void OnDecideDownloadDestination(object? sender, WKDownloadDestinationEventArgs e)
    {
        _ = _pendingDownloadPaths.TryDequeue(out var path);
        e.Decide(TryCreateFileUrl(path));
    }

    private void OnDidFinishNavigation(object? sender, EventArgs e)
    {
        OnNavigationTerminal(NavigationCompletedStatus.Success, error: null, requestUriOverride: TryGetUri(_webView?.Url));
    }

    private void OnDidFailNavigation(object? sender, NSError error)
    {
        var navId = _activeNavigationId;
        var navUri = TryGetUri(_webView?.Url) ?? _activeRequestUri ?? new Uri("about:blank");
        OnNavigationTerminal(
            NavigationCompletedStatus.Failure,
            NSError.ToException(error.Handle),
            navUri);
    }

    private void OnDidReceiveServerTrustChallenge(object? sender, ServerTrustChallengeEventArgs e)
    {
        var requestUri = _activeRequestUri ?? new Uri($"https://{e.Host}/");
        var ctx = new ServerCertificateErrorContext(
            requestUri,
            e.Host,
            e.ErrorSummary,
            e.PlatformRawCode,
            e.CertificateSubject,
            e.CertificateIssuer,
            e.ValidFrom,
            e.ValidTo);

        _ = _securityHooks.OnServerCertificateError(ctx);
        OnNavigationTerminal(
            NavigationCompletedStatus.Failure,
            new WebViewSslException(ctx, _activeNavigationId),
            requestUri);
    }

    private void OnScriptMessageReceived(object? sender, WKScriptMessageEventArgs e)
    {
        if (_detached)
        {
            return;
        }

        var body = ObjCValueToString(e.Message.Body);
        var origin = WKFrameInfo.TryGetOriginString(e.Message.FrameInfo) ?? string.Empty;
        var channelId = _host?.ChannelId ?? Guid.Empty;
        SafeRaise(() => WebMessageReceived?.Invoke(this, new WebMessageReceivedEventArgs(body, origin, channelId, protocolVersion: 1)));
    }

    private void OnRequestMediaCapturePermission(object? sender, MediaCapturePermissionEventArgs e)
    {
        var kind = e.MediaCaptureType switch
        {
            0 => WebViewPermissionKind.Camera,
            1 => WebViewPermissionKind.Microphone,
            2 => WebViewPermissionKind.Camera,
            _ => WebViewPermissionKind.Unknown
        };
        _ = Uri.TryCreate(WKSecurityOrigin.TryGetOriginString(e.Origin), UriKind.Absolute, out var origin);
        var args = new PermissionRequestedEventArgs(kind, origin);
        SafeRaise(() => PermissionRequested?.Invoke(this, args));
        e.Decision = args.State switch
        {
            PermissionState.Allow => WKPermissionDecision.Grant,
            PermissionState.Deny => WKPermissionDecision.Deny,
            _ => WKPermissionDecision.Prompt
        };
    }

    private void OnSchemeStartTask(object? sender, WKURLSchemeTaskEventArgs e)
    {
        var request = e.Task.Request;
        var uri = TryGetUri(request.Url);
        if (uri is null)
        {
            CompleteSchemeTask(e.Task, request, "text/plain", []);
            return;
        }

        var args = new WebResourceRequestedEventArgs(uri, request.HTTPMethod.GetString() ?? "GET");
        SafeRaise(() => WebResourceRequested?.Invoke(this, args));
        if (!args.Handled || args.ResponseBody is null)
        {
            CompleteSchemeTask(e.Task, request, "text/plain", []);
            return;
        }

        using var memory = new MemoryStream();
        args.ResponseBody.CopyTo(memory);
        CompleteSchemeTask(e.Task, request, args.ResponseContentType, memory.ToArray());
    }

    private static void CompleteSchemeTask(WKURLSchemeTask task, NSURLRequest request, string contentType, byte[] body)
    {
        using var response = NSURLResponse.Create(request.Url, contentType, body.LongLength, textEncodingName: null);
        using var data = NSData.FromBytes(body);
        task.DidReceiveResponse(response);
        task.DidReceiveData(data);
        task.DidFinish();
    }

    private static void OnSchemeStopTask(object? sender, WKURLSchemeTaskEventArgs e)
    {
    }

    private void AddUserScript(string javaScript)
    {
        using var source = NSString.Create(javaScript)!;
        using var script = new WKUserScript(source, WKUserScriptInjectionTime.AtDocumentStart, forMainFrameOnly: true);
        _userContentController!.AddUserScript(script);
    }

    private void BeginApiNavigation(Guid navigationId, Uri requestUri)
    {
        _apiNavigationActive = true;
        _activeNavigationId = navigationId;
        _activeRequestUri = requestUri;
        _activeNavigationCompleted = false;
        _nativeCorrelationActive = false;
        _nativeCorrelationId = Guid.Empty;
    }

    private void CompleteCanceledFromPolicyDecision(Guid navigationId, Uri requestUri)
    {
        if (_detached)
        {
            return;
        }

        lock (_navLock)
        {
            if (_activeNavigationId == navigationId && _activeNavigationCompleted)
            {
                return;
            }

            _activeNavigationId = navigationId;
            _activeRequestUri = requestUri;
            _activeNavigationCompleted = true;
        }

        RaiseNavigationCompleted(navigationId, requestUri, NavigationCompletedStatus.Canceled, error: null);
        lock (_navLock)
        {
            ClearNativeCorrelationIfNeeded();
            _apiNavigationActive = false;
        }
    }

    private void OnNavigationTerminal(NavigationCompletedStatus status, Exception? error, Uri? requestUriOverride)
    {
        if (_detached)
        {
            return;
        }

        Guid navId;
        Uri requestUri;
        lock (_navLock)
        {
            if (_activeNavigationId == Guid.Empty || _activeNavigationCompleted)
            {
                ClearNativeCorrelationIfNeeded();
                _apiNavigationActive = false;
                return;
            }

            _activeNavigationCompleted = true;
            navId = _activeNavigationId;
            requestUri = requestUriOverride ?? _activeRequestUri ?? new Uri("about:blank");
        }

        RaiseNavigationCompleted(navId, requestUri, status, error);
        lock (_navLock)
        {
            ClearNativeCorrelationIfNeeded();
            _apiNavigationActive = false;
        }
    }

    private Guid GetOrCreateNativeCorrelationId(int navigationType, bool continuation)
    {
        if (!_nativeCorrelationActive)
        {
            _nativeCorrelationActive = true;
            _nativeCorrelationId = Guid.NewGuid();
            return _nativeCorrelationId;
        }

        if (continuation)
        {
            return _nativeCorrelationId;
        }

        if (navigationType is 0 or 1 or 4 or < 0)
        {
            _nativeCorrelationId = Guid.NewGuid();
        }

        return _nativeCorrelationId;
    }

    private void ClearNavigationState()
    {
        lock (_navLock)
        {
            _activeNavigationId = Guid.Empty;
            _activeRequestUri = null;
            _activeNavigationCompleted = false;
            _apiNavigationActive = false;
            ClearNativeCorrelationIfNeeded();
        }
    }

    private void ClearNativeCorrelationIfNeeded()
    {
        _nativeCorrelationActive = false;
        _nativeCorrelationId = Guid.Empty;
    }

    private void RaiseNavigationCompleted(Guid navigationId, Uri requestUri, NavigationCompletedStatus status, Exception? error)
        => SafeRaise(() => NavigationCompleted?.Invoke(this, new NavigationCompletedEventArgs(navigationId, requestUri, status, error)));

    private void ThrowIfNotInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Adapter must be initialized before use.");
        }
    }

    private void ThrowIfNotAttached()
    {
        ThrowIfNotInitialized();
        ObjectDisposedException.ThrowIf(_detached, nameof(MacOSWebViewAdapter));
        if (!_attached || _webView is null)
        {
            throw new InvalidOperationException("Adapter must be attached before use.");
        }
    }

    private void ThrowIfNotAttachedForCookies()
    {
        ObjectDisposedException.ThrowIf(_detached, nameof(MacOSWebViewAdapter));
        if (!_attached || _dataStore is null)
        {
            throw new InvalidOperationException("Adapter is not attached.");
        }
    }

    private static Uri? TryGetUri(NSUrl? url)
        => url?.AbsoluteString is { } text && Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;

    private static NSUrl? TryCreateFileUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fileUri = new Uri(Path.GetFullPath(path));
        using var nsString = NSString.Create(fileUri.AbsoluteUri);
        return new NSUrl(nsString);
    }

    private static string JsStringLiteral(string value)
        => "'" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "'";

    private static bool IsLikelyDownload(string? mimeType)
        => string.IsNullOrWhiteSpace(mimeType) ||
           mimeType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
           mimeType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase) ||
           mimeType.StartsWith("application/zip", StringComparison.OrdinalIgnoreCase);

    private static string? JsResultToString(NSObject? result)
        => result is null ? null : ObjCValueToString(result.Handle);

    private static string ObjCValueToString(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            return string.Empty;
        }

        if (NSString.TryGetString(value) is { } str)
        {
            return str;
        }

        if (NSNumber.TryAsStringValue(value) is { } number)
        {
            return number;
        }

        return NSObject.GetDescription(value) ?? string.Empty;
    }

    private static void SafeRaise(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Keep native WebKit callbacks exception-safe.
        }
    }

    private sealed record AppleWKWebViewPlatformHandle(nint WKWebViewHandle) : IAppleWKWebViewPlatformHandle
    {
        public nint Handle => WKWebViewHandle;
        public string HandleDescriptor => "WKWebView";
    }
}
