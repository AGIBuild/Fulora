using System.Runtime.Versioning;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using AndroidX.Activity;
using Java.Interop;
using AWebView = Android.Webkit.WebView;
using JavaObject = Java.Lang.Object;

namespace Agibuild.Fulora.Adapters.Android;

[SupportedOSPlatform("android")]
internal sealed class AndroidWebViewAdapter : IWebViewAdapter, INativeWebViewHandleProvider, ICookieAdapter, IWebViewAdapterOptions,
    ICustomSchemeAdapter, IDownloadAdapter, IPermissionAdapter, ICommandAdapter, IScreenshotAdapter, IPrintAdapter,
    IFindInPageAdapter, IZoomAdapter, IPreloadScriptAdapter, IContextMenuAdapter, IDevToolsAdapter
{
    private static bool DiagnosticsEnabled
        => string.Equals(System.Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal);

    private IWebViewAdapterHost? _host;

    private bool _initialized;
    private bool _attached;
    private bool _detached;

    // Android WebView objects
    private AWebView? _webView;
    private AdapterWebViewClient? _webViewClient;
    private AdapterWebChromeClient? _webChromeClient;
    private AndroidJsBridge? _jsBridge;
    private Handler? _mainHandler;
    private OnBackPressedCallback? _backCallback;

    // Signals when InitializeWebView has completed on the UI thread.
    private readonly TaskCompletionSource _webViewReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Navigation state
    private readonly object _navLock = new();

    // API-initiated navigation tracking
    private Guid _pendingApiNavigationId;
    private bool _pendingApiNavigation;
    private readonly HashSet<string> _apiNavUrls = new(); // URLs from API calls currently in flight
    private readonly Dictionary<string, Guid> _apiNavIdByUrl = new(); // URL → API NavigationId

    // Native-initiated navigation tracking
    private Guid _currentCorrelationId;
    private Guid _currentNativeNavigationId;
    private bool _hasActiveNavigation;
    private string? _activeNavigationUrl;

    // Guard exactly-once completion per NavigationId
    private readonly HashSet<Guid> _completedNavIds = new();

    // Tracks whether current navigation had an error (set in OnReceivedError, consumed in OnPageFinished)
    private bool _navigationErrorOccurred;
    private Exception? _navigationError;
    private Guid _navigationErrorNavId;

    // Environment options (stored before Attach, applied after WebView creation)
    private IWebViewEnvironmentOptions? _pendingOptions;

    public bool CanGoBack => _webView?.CanGoBack() ?? false;
    public bool CanGoForward => _webView?.CanGoForward() ?? false;

    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;

    private IReadOnlyList<CustomSchemeRegistration>? _customSchemes;
    private HashSet<string>? _registeredSchemes;

    public void RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes)
    {
        _customSchemes = schemes;
        _registeredSchemes = new HashSet<string>(
            schemes.Select(s => s.SchemeName?.ToLowerInvariant() ?? string.Empty).Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);
    }

    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested
    {
        add { }
        remove { }
    }

    // ==================== Lifecycle ====================

    public void Initialize(IWebViewAdapterHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (_initialized)
        {
            throw new InvalidOperationException($"{nameof(Initialize)} can only be called once.");
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

        if (!OperatingSystem.IsAndroid())
        {
            throw new PlatformNotSupportedException("Android WebView adapter can only be used on Android.");
        }

        _attached = true;
        _mainHandler = new Handler(Looper.MainLooper!);

        // Resolve the parent ViewGroup from the platform handle.
        // On Avalonia Android, the IPlatformHandle.Handle is a GCHandle pointer to the native View.
        var parentView = ResolveParentView(parentHandle);

        // Create WebView on the UI thread.
        PostOnUiThread(() => InitializeWebView(parentView));
    }

    private void InitializeWebView(ViewGroup parentView)
    {
        // Guard against race: Detach() may have been called before this posted action executes.
        if (_detached)
        {
            _webViewReady.TrySetCanceled();
            return;
        }

        try
        {
            var context = parentView.Context
                          ?? global::Android.App.Application.Context;

            _webView = new AWebView(context);

            // Configure WebSettings
            var settings = _webView.Settings!;
            settings.JavaScriptEnabled = true;
            settings.DomStorageEnabled = true;
            settings.AllowContentAccess = true;
            settings.AllowFileAccess = false;
            settings.MixedContentMode = MixedContentHandling.CompatibilityMode;
            settings.SetSupportMultipleWindows(true);

            // Apply pending options
            ApplyPendingOptions();

            // Create and attach WebViewClient
            _webViewClient = new AdapterWebViewClient(this);
            _webView.SetWebViewClient(_webViewClient);

            // Create and attach WebChromeClient
            _webChromeClient = new AdapterWebChromeClient(this);
            _webView.SetWebChromeClient(_webChromeClient);

            // Set up download listener
            _webView.SetDownloadListener(new AdapterDownloadListener(this));

            // Set up JavaScript bridge for WebMessage receive path
            var channelId = _host?.ChannelId ?? Guid.Empty;
            _jsBridge = new AndroidJsBridge(this, channelId);
            _webView.AddJavascriptInterface(_jsBridge, "__agibuildBridge");

            // Set layout params and add to parent
            _webView.LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent);
            parentView.AddView(_webView);

            // Register system back key handler: GoBack in WebView when history exists.
            RegisterBackCallback(parentView.Context);

            if (DiagnosticsEnabled)
            {
                Console.WriteLine("[Agibuild.WebView] Android WebView initialized successfully.");
            }

            _webViewReady.TrySetResult();
        }
        catch (Exception ex)
        {
            _webViewReady.TrySetException(ex);

            if (DiagnosticsEnabled)
            {
                Console.WriteLine($"[Agibuild.WebView] Android WebView initialization failed: {ex.Message}");
            }
        }
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

        // Cancel any pending _webViewReady waiters so navigation methods don't hang.
        _webViewReady.TrySetCanceled();

        // Clear navigation state immediately (no UI thread needed).
        lock (_navLock)
        {
            _apiNavUrls.Clear();
            _apiNavIdByUrl.Clear();
            _completedNavIds.Clear();
            _pendingApiNavigation = false;
            _hasActiveNavigation = false;
        }

        // Remove back-key callback to prevent leaking the Activity reference.
        _backCallback?.Remove();
        _backCallback = null;

        // Capture references for the cleanup closure.
        var webView = _webView;
        var jsBridge = _jsBridge;

        // Null out references immediately so no further calls can use them.
        _webView = null;
        _webViewClient = null;
        _webChromeClient = null;
        _jsBridge = null;

        // Post WebView teardown asynchronously to avoid blocking the Avalonia render pass.
        // Android WebView.Destroy() can be slow (5s+) when content is loaded.
        PostOnUiThread(() =>
        {
            try
            {
                if (webView is not null)
                {
                    webView.StopLoading();
                    webView.SetWebViewClient(null!);
                    webView.SetWebChromeClient(null!);
                    webView.SetDownloadListener(null);

                    if (jsBridge is not null)
                    {
                        webView.RemoveJavascriptInterface("__agibuildBridge");
                    }

                    // Remove from parent
                    if (webView.Parent is ViewGroup parent)
                    {
                        parent.RemoveView(webView);
                    }

                    webView.Destroy();
                }
            }
            catch
            {
                // Swallow exceptions during teardown — the control is already detached.
            }
        });
    }

    private void RegisterBackCallback(global::Android.Content.Context? context)
    {
        // Walk up to the ComponentActivity that owns this view.
        var activity = context as ComponentActivity
                       ?? (context as global::Android.Content.ContextWrapper)
                           ?.BaseContext as ComponentActivity;

        if (activity is null) return;

        _backCallback = new WebViewBackCallback(this);
        activity.OnBackPressedDispatcher.AddCallback(activity, _backCallback);
    }

    /// <summary>
    /// Updates <see cref="_backCallback"/> enabled state so the system back key
    /// is only intercepted when the WebView can actually go back.
    /// Called from <see cref="AdapterWebViewClient.OnPageFinished"/>.
    /// </summary>
    internal void SyncBackCallbackEnabled()
    {
        if (_backCallback is not null)
        {
            _backCallback.Enabled = _webView?.CanGoBack() ?? false;
        }
    }

    /// <summary>
    /// <see cref="OnBackPressedCallback"/> that delegates to the WebView's GoBack().
    /// </summary>
    private sealed class WebViewBackCallback : OnBackPressedCallback
    {
        private readonly AndroidWebViewAdapter _adapter;

        public WebViewBackCallback(AndroidWebViewAdapter adapter) : base(false)
        {
            _adapter = adapter;
        }

        public override void HandleOnBackPressed()
        {
            var wv = _adapter._webView;
            if (wv is not null && wv.CanGoBack())
            {
                wv.GoBack();
            }
            else
            {
                // Nothing to go back to — disable ourselves so the system handles the back press.
                Enabled = false;
                // Re-dispatch: the framework will call the next callback or finish the Activity.
                _adapter._mainHandler?.Post(() =>
                {
                    // Trigger back press again; we're now disabled so it won't loop.
                    if (_adapter._webView?.Context is ComponentActivity act)
                    {
                        act.OnBackPressedDispatcher.OnBackPressed();
                    }
                });
            }
        }
    }

    private void ApplyPendingOptions()
    {
        if (_webView is null || _pendingOptions is null) return;

        if (_pendingOptions.EnableDevTools)
        {
            AWebView.SetWebContentsDebuggingEnabled(true);
        }

        if (_pendingOptions.CustomUserAgent is not null)
        {
            _webView.Settings!.UserAgentString = _pendingOptions.CustomUserAgent;
        }
    }

    // ==================== Navigation — API-initiated ====================

    public async Task NavigateAsync(Guid navigationId, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ThrowIfNotAttached();

        // Wait for the native WebView to be created (Attach posts asynchronously).
        await _webViewReady.Task.ConfigureAwait(false);

        // Bundle BeginApiNavigation + LoadUrl into a single UI-thread action.
        // After the await above we may be on a thread-pool thread. If we arm the
        // navigation tracker first and then post LoadUrl separately, a stale
        // onPageFinished callback (from a previous navigation) can fire on the
        // Android UI thread between the two calls, prematurely completing the
        // tracker with the wrong URL.
        var absoluteUri = uri.AbsoluteUri;
        RunOnUiThread(() =>
        {
            lock (_navLock) { BeginApiNavigation(navigationId, absoluteUri); }
            _webView?.LoadUrl(absoluteUri);
        });
    }

    public Task NavigateToStringAsync(Guid navigationId, string html)
        => NavigateToStringAsync(navigationId, html, baseUrl: null);

    public async Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ThrowIfNotAttached();

        // Wait for the native WebView to be created (Attach posts asynchronously).
        await _webViewReady.Task.ConfigureAwait(false);

        // Always use LoadDataWithBaseURL — Android's LoadData has known encoding bugs
        // with '%', '#', non-ASCII characters, and emoji in HTML content.
        var baseUrlStr = baseUrl?.AbsoluteUri;
        // When baseUrl is null, Android reports "about:blank" in onPageStarted/onPageFinished.
        var navKey = baseUrlStr ?? "about:blank";

        // Bundle BeginApiNavigation + LoadDataWithBaseURL into a single UI-thread
        // action to prevent stale onPageFinished callbacks from prematurely completing
        // the navigation tracker (see NavigateAsync for full explanation).
        RunOnUiThread(() =>
        {
            lock (_navLock) { BeginApiNavigation(navigationId, navKey); }
            _webView?.LoadDataWithBaseURL(baseUrlStr, html, "text/html", "UTF-8", null);
        });
    }

    // ==================== Navigation — commands ====================

    public bool GoBack(Guid navigationId)
    {
        ThrowIfNotAttached();
        if (_webView is null || !_webView.CanGoBack()) return false;
        lock (_navLock) { BeginApiNavigation(navigationId, null); }
        RunOnUiThread(() => _webView?.GoBack());
        return true;
    }

    public bool GoForward(Guid navigationId)
    {
        ThrowIfNotAttached();
        if (_webView is null || !_webView.CanGoForward()) return false;
        lock (_navLock) { BeginApiNavigation(navigationId, null); }
        RunOnUiThread(() => _webView?.GoForward());
        return true;
    }

    public bool Refresh(Guid navigationId)
    {
        ThrowIfNotAttached();
        if (_webView is null) return false;
        lock (_navLock) { BeginApiNavigation(navigationId, null); }
        RunOnUiThread(() => _webView?.Reload());
        return true;
    }

    public bool Stop()
    {
        ThrowIfNotAttached();
        RunOnUiThread(() => _webView?.StopLoading());
        return true;
    }

    // ==================== Script execution ====================

    public async Task<string?> InvokeScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        ThrowIfNotAttached();

        // Wait for the native WebView to be created (Attach posts asynchronously).
        await _webViewReady.Task.ConfigureAwait(false);

        if (_webView is null)
        {
            throw new InvalidOperationException("WebView is not available.");
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnUiThread(() =>
        {
            _webView.EvaluateJavascript(script, new ScriptResultCallback(tcs));
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    // ==================== ICookieAdapter ====================

    public Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri)
    {
        ThrowIfNotAttachedForCookies();

        var cookieManager = CookieManager.Instance;
        if (cookieManager is null)
        {
            return Task.FromResult<IReadOnlyList<WebViewCookie>>(Array.Empty<WebViewCookie>());
        }

        var cookieString = cookieManager.GetCookie(uri.AbsoluteUri);
        var result = ParseCookieString(cookieString, uri);
        return Task.FromResult<IReadOnlyList<WebViewCookie>>(result);
    }

    public Task SetCookieAsync(WebViewCookie cookie)
    {
        ThrowIfNotAttachedForCookies();

        var cookieManager = CookieManager.Instance;
        if (cookieManager is null)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cookieString = FormatCookieString(cookie);
        var url = $"https://{cookie.Domain}{cookie.Path}";

        cookieManager.SetCookie(url, cookieString, new CookieValueCallback(tcs));
        cookieManager.Flush();
        return tcs.Task;
    }

    public Task DeleteCookieAsync(WebViewCookie cookie)
    {
        ThrowIfNotAttachedForCookies();

        var cookieManager = CookieManager.Instance;
        if (cookieManager is null)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Android has no direct delete API — set with expired date.
        var expiredCookie = $"{cookie.Name}=; Domain={cookie.Domain}; Path={cookie.Path}; Expires=Thu, 01 Jan 1970 00:00:00 GMT";
        var url = $"https://{cookie.Domain}{cookie.Path}";

        cookieManager.SetCookie(url, expiredCookie, new CookieValueCallback(tcs));
        cookieManager.Flush();
        return tcs.Task;
    }

    public Task ClearAllCookiesAsync()
    {
        ThrowIfNotAttachedForCookies();

        var cookieManager = CookieManager.Instance;
        if (cookieManager is null)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cookieManager.RemoveAllCookies(new CookieValueCallback(tcs));
        cookieManager.Flush();
        return tcs.Task;
    }

    // ==================== INativeWebViewHandleProvider ====================

    public INativeHandle? TryGetWebViewHandle()
    {
        if (!_attached || _detached || _webView is null) return null;

        return new AndroidWebViewPlatformHandle(_webView.Handle);
    }

    /// <summary>Typed platform handle for Android WebView.</summary>
    private sealed record AndroidWebViewPlatformHandle(nint AndroidWebViewHandle) : IAndroidWebViewPlatformHandle
    {
        public nint Handle => AndroidWebViewHandle;
        public string HandleDescriptor => "AndroidWebView";
    }

    // ==================== IWebViewAdapterOptions ====================

    public void ApplyEnvironmentOptions(IWebViewEnvironmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfNotInitialized();

        if (_webView is not null)
        {
            RunOnUiThread(() =>
            {
                if (options.EnableDevTools)
                {
                    AWebView.SetWebContentsDebuggingEnabled(true);
                }

                if (options.CustomUserAgent is not null)
                {
                    _webView.Settings!.UserAgentString = options.CustomUserAgent;
                }
            });
        }
        else
        {
            _pendingOptions = options;
        }
    }

    public void SetCustomUserAgent(string? userAgent)
    {
        ThrowIfNotInitialized();
        if (_webView is not null)
        {
            RunOnUiThread(() => _webView.Settings!.UserAgentString = userAgent ?? string.Empty);
        }
    }

    // ==================== WebViewClient callbacks ====================

    internal void OnShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request)
    {
        // This is called from AdapterWebViewClient.ShouldOverrideUrlLoading
        // and only for navigations we need to intercept.
    }

    internal WebResourceResponse? HandleShouldInterceptRequest(IWebResourceRequest? request)
    {
        if (_detached || request?.Url is null || _registeredSchemes is null)
            return null;

        var scheme = request.Url.Scheme;
        if (string.IsNullOrEmpty(scheme) || !_registeredSchemes.Contains(scheme))
            return null;

        if (!Uri.TryCreate(request.Url.ToString(), UriKind.Absolute, out var uri))
            return null;

        var headers = request.RequestHeaders is not null
            ? new Dictionary<string, string>(request.RequestHeaders as IDictionary<string, string> ?? new Dictionary<string, string>())
            : null;

        var args = new WebResourceRequestedEventArgs(uri, request.Method ?? "GET", headers?.AsReadOnly());

        WebResourceRequested?.Invoke(this, args);

        if (!args.Handled || args.ResponseBody is null)
            return null;

        var mime = args.ResponseContentType ?? "application/octet-stream";
        var statusCode = args.ResponseStatusCode > 0 ? args.ResponseStatusCode : 200;
        var encoding = "UTF-8";

        var responseHeaders = new Dictionary<string, string>
        {
            ["Access-Control-Allow-Origin"] = "*"
        };
        if (args.ResponseHeaders is not null)
        {
            foreach (var kvp in args.ResponseHeaders)
            {
                responseHeaders[kvp.Key] = kvp.Value;
            }
        }

        return new WebResourceResponse(
            mime, encoding, statusCode, "OK",
            responseHeaders!, args.ResponseBody);
    }

    internal bool HandleShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request)
    {
        if (_detached || request?.Url is null) return false;

        var url = request.Url.ToString() ?? string.Empty;

        lock (_navLock)
        {
            // Check if this is an API-initiated navigation.
            if (_apiNavUrls.Contains(url))
            {
                if (DiagnosticsEnabled)
                {
                    Console.WriteLine($"[Agibuild.WebView] ShouldOverrideUrlLoading (API): url={url}");
                }
                return false; // Allow API navigation to proceed.
            }

            // Also check if we have a pending API navigation without URL tracking
            // (e.g., GoBack/GoForward/Refresh).
            if (_pendingApiNavigation)
            {
                _pendingApiNavigation = false;
                _hasActiveNavigation = true;
                _activeNavigationUrl = url;
                _apiNavIdByUrl[url] = _pendingApiNavigationId;
                _apiNavUrls.Add(url);

                if (DiagnosticsEnabled)
                {
                    Console.WriteLine($"[Agibuild.WebView] ShouldOverrideUrlLoading (API pending): url={url}, navId={_pendingApiNavigationId}");
                }
                return false;
            }
        }

        // Native-initiated navigation — consult the host.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var requestUri))
        {
            return false;
        }

        var host = _host;
        if (host is null)
        {
            return true; // Cancel if no host.
        }

        Guid correlationId;
        lock (_navLock)
        {
            // New native navigation chain.
            correlationId = Guid.NewGuid();
            _currentCorrelationId = correlationId;
        }

        var info = new NativeNavigationStartingInfo(correlationId, requestUri, IsMainFrame: true);
        var decisionTask = host.OnNativeNavigationStartingAsync(info);

        var decision = decisionTask.IsCompleted
            ? decisionTask.Result
            : decisionTask.AsTask().GetAwaiter().GetResult();

        if (DiagnosticsEnabled)
        {
            Console.WriteLine($"[Agibuild.WebView] ShouldOverrideUrlLoading (native): url={url}, allowed={decision.IsAllowed}, navId={decision.NavigationId}");
        }

        if (!decision.IsAllowed)
        {
            if (decision.NavigationId != Guid.Empty)
            {
                RaiseNavigationCompleted(decision.NavigationId, requestUri, NavigationCompletedStatus.Canceled, error: null);
            }
            return true; // Cancel the navigation.
        }

        if (decision.NavigationId != Guid.Empty)
        {
            lock (_navLock)
            {
                _currentNativeNavigationId = decision.NavigationId;
                _hasActiveNavigation = true;
                _activeNavigationUrl = url;
            }
        }

        return false; // Allow the navigation.
    }

    internal void OnPageStarted(AWebView? view, string? url)
    {
        if (_detached || url is null) return;

        lock (_navLock)
        {
            // Inject WebMessage bridge script on each page load.
            InjectBridgeScript(view);
            InjectPreloadScripts(view);

            if (!_hasActiveNavigation) return;

            // URL change during active navigation indicates a redirect.
            if (_activeNavigationUrl is not null && !string.Equals(_activeNavigationUrl, url, StringComparison.Ordinal))
            {
                // Redirect detected — re-register the navigation ID under the new URL
                // so OnPageFinished can match it by exact URL lookup.
                if (_apiNavIdByUrl.TryGetValue(_activeNavigationUrl, out var redirectedNavId))
                {
                    _apiNavIdByUrl.Remove(_activeNavigationUrl);
                    _apiNavUrls.Remove(_activeNavigationUrl);
                    _apiNavIdByUrl[url] = redirectedNavId;
                    _apiNavUrls.Add(url);
                }

                _activeNavigationUrl = url;

                if (DiagnosticsEnabled)
                {
                    Console.WriteLine($"[Agibuild.WebView] OnPageStarted (redirect): url={url}, correlationId={_currentCorrelationId}");
                }
            }
        }
    }

    internal void OnPageFinished(AWebView? view, string? url)
    {
        if (_detached) return;

        Guid navigationId;
        Uri requestUri;

        lock (_navLock)
        {
            if (!_hasActiveNavigation) return;

            // Determine NavigationId — either from API or native tracking.
            if (url is not null && _apiNavIdByUrl.TryGetValue(url, out var apiNavId))
            {
                navigationId = apiNavId;
                _apiNavIdByUrl.Remove(url);
                _apiNavUrls.Remove(url);
            }
            else if (_currentNativeNavigationId != Guid.Empty)
            {
                navigationId = _currentNativeNavigationId;
            }
            else if (_pendingApiNavigation)
            {
                navigationId = _pendingApiNavigationId;
                _pendingApiNavigation = false;
            }
            else
            {
                // No URL match, no native navigation ID, no pending API navigation.
                // This is a late/stale onPageFinished from a previous page load — ignore it.
                if (DiagnosticsEnabled)
                {
                    Console.WriteLine($"[Agibuild.WebView] OnPageFinished (ignored stale): url={url}");
                }
                return;
            }

            // Exactly-once guard.
            if (!_completedNavIds.Add(navigationId))
            {
                return;
            }

            // Clean up.
            _hasActiveNavigation = false;
            _currentNativeNavigationId = Guid.Empty;
            _activeNavigationUrl = null;
        }

        requestUri = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : new Uri("about:blank");

        // Check if an error was reported for this navigation.
        if (_navigationErrorOccurred && _navigationErrorNavId == navigationId)
        {
            _navigationErrorOccurred = false;
            var error = _navigationError;
            _navigationError = null;
            RaiseNavigationCompleted(navigationId, requestUri, NavigationCompletedStatus.Failure, error);
        }
        else
        {
            _navigationErrorOccurred = false;
            _navigationError = null;
            RaiseNavigationCompleted(navigationId, requestUri, NavigationCompletedStatus.Success, error: null);
        }

        // Update back-key callback state after each navigation finishes.
        SyncBackCallbackEnabled();
    }

    internal void OnReceivedError(AWebView? view, IWebResourceRequest? request, WebResourceError? error)
    {
        if (_detached || request is null || error is null) return;

        // Only handle main frame errors.
        if (!request.IsForMainFrame) return;

        var url = request.Url?.ToString() ?? string.Empty;
        var errorCode = (ClientError)error.ErrorCode;

        Guid navigationId;
        lock (_navLock)
        {
            // Determine the NavigationId for this error.
            if (url.Length > 0 && _apiNavIdByUrl.TryGetValue(url, out var apiNavId))
            {
                navigationId = apiNavId;
            }
            else if (_currentNativeNavigationId != Guid.Empty)
            {
                navigationId = _currentNativeNavigationId;
            }
            else if (_pendingApiNavigation)
            {
                navigationId = _pendingApiNavigationId;
            }
            else
            {
                // Try any tracked API nav
                navigationId = _apiNavIdByUrl.Values.FirstOrDefault();
                if (navigationId == Guid.Empty) return;
            }
        }

        var requestUri = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : new Uri("about:blank");
        var errorMessage = $"Navigation failed: {errorCode} - {error.Description}";
        var exception = MapErrorCode(errorCode, errorMessage, navigationId, requestUri);

        // Store the error — it will be consumed in OnPageFinished (Android calls both).
        _navigationErrorOccurred = true;
        _navigationError = exception;
        _navigationErrorNavId = navigationId;
    }

    internal void OnNewWindowRequested(AWebView? view, string? url)
    {
        if (_detached) return;

        Uri? targetUri = null;
        if (!string.IsNullOrWhiteSpace(url))
        {
            Uri.TryCreate(url, UriKind.Absolute, out targetUri);
        }

        var args = new NewWindowRequestedEventArgs(targetUri);
        SafeRaise(() => NewWindowRequested?.Invoke(this, args));
    }

    internal void OnWebMessageReceived(string body, string origin)
    {
        if (_detached) return;

        var channelId = _host?.ChannelId ?? Guid.Empty;
        SafeRaise(() => WebMessageReceived?.Invoke(this,
            new WebMessageReceivedEventArgs(body, origin, channelId, protocolVersion: 1)));
    }

    // ==================== Private helpers ====================

    private void BeginApiNavigation(Guid navigationId, string? url)
    {
        _hasActiveNavigation = true;
        _currentNativeNavigationId = Guid.Empty;

        if (url is not null)
        {
            // URL is known upfront (NavigateAsync, NavigateToStringAsync).
            // Register by exact URL so OnPageFinished can match precisely.
            // Do NOT set _pendingApiNavigation — that flag is reserved for
            // GoBack/GoForward/Refresh where the URL is unknown until
            // ShouldOverrideUrlLoading fires.
            _pendingApiNavigation = false;
            _apiNavUrls.Add(url);
            _apiNavIdByUrl[url] = navigationId;
            _activeNavigationUrl = url;
        }
        else
        {
            // URL unknown (GoBack/GoForward/Refresh).
            // ShouldOverrideUrlLoading will resolve the actual URL.
            _pendingApiNavigation = true;
            _pendingApiNavigationId = navigationId;
        }
    }

    private void InjectBridgeScript(AWebView? view)
    {
        if (view is null) return;

        var channelId = _host?.ChannelId ?? Guid.Empty;
        // Pass the message body directly to the Java bridge without wrapping in a
        // JSON envelope. The C# side (OnWebMessageReceived) already sets channelId
        // and protocolVersion independently. Double-wrapping broke RPC/Bridge
        // because the runtime layer received the envelope string instead of the
        // actual message body.
        var bridgeScript = WebViewBridgeScriptFactory.CreateAndroidBridgeBootstrapScript(channelId);

        view.EvaluateJavascript(bridgeScript, null);
    }

    private void InjectPreloadScripts(AWebView? view)
    {
        if (view is null || _preloadScripts.Count == 0) return;

        foreach (var script in _preloadScripts.Values)
        {
            view.EvaluateJavascript(script, null);
        }
    }

    private static Exception MapErrorCode(ClientError errorCode, string message, Guid navigationId, Uri requestUri)
    {
        var category = errorCode switch
        {
            ClientError.Timeout
                => NavigationErrorCategory.Timeout,

            ClientError.HostLookup or
            ClientError.Connect or
            ClientError.Io or
            ClientError.Unknown
                => NavigationErrorCategory.Network,

            ClientError.FailedSslHandshake or
            ClientError.Authentication
                => NavigationErrorCategory.Ssl,

            _ => NavigationErrorCategory.Other,
        };

        return NavigationErrorFactory.Create(category, message, navigationId, requestUri);
    }

    private static ViewGroup ResolveParentView(INativeHandle parentHandle)
    {
        if (parentHandle.Handle == IntPtr.Zero)
        {
            throw new ArgumentException("Parent handle must be non-zero.", nameof(parentHandle));
        }

        // On Avalonia Android, the handle is a JNI reference to the Android View.
        var parentObj = JavaObject.GetObject<View>(parentHandle.Handle, JniHandleOwnership.DoNotTransfer);
        if (parentObj is ViewGroup viewGroup)
        {
            return viewGroup;
        }

        throw new ArgumentException(
            $"Parent handle must resolve to an Android ViewGroup, got: {parentObj?.GetType().Name ?? "null"}",
            nameof(parentHandle));
    }

    private static List<WebViewCookie> ParseCookieString(string? cookieString, Uri uri)
    {
        var result = new List<WebViewCookie>();
        if (string.IsNullOrWhiteSpace(cookieString)) return result;

        // Android CookieManager.GetCookie returns "name=value; name2=value2" format.
        var pairs = cookieString.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0) continue;

            var name = pair[..eqIndex].Trim();
            var value = pair[(eqIndex + 1)..].Trim();

            result.Add(new WebViewCookie(
                name, value,
                uri.Host,
                uri.AbsolutePath.Length > 0 ? "/" : uri.AbsolutePath,
                Expires: null,
                IsSecure: false,
                IsHttpOnly: false));
        }

        return result;
    }

    private static string FormatCookieString(WebViewCookie cookie)
    {
        var parts = new List<string>
        {
            $"{cookie.Name}={cookie.Value}",
            $"Domain={cookie.Domain}",
            $"Path={cookie.Path}"
        };

        if (cookie.Expires.HasValue)
        {
            parts.Add($"Expires={cookie.Expires.Value.UtcDateTime:R}");
        }

        if (cookie.IsSecure)
        {
            parts.Add("Secure");
        }

        if (cookie.IsHttpOnly)
        {
            parts.Add("HttpOnly");
        }

        return string.Join("; ", parts);
    }

    private void PostOnUiThread(Action action)
    {
        if (_mainHandler is not null)
        {
            _mainHandler.Post(action);
        }
        else
        {
            action();
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (Looper.MainLooper == Looper.MyLooper())
        {
            action();
        }
        else if (_mainHandler is not null)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _mainHandler.Post(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            tcs.Task.GetAwaiter().GetResult();
        }
        else
        {
            action();
        }
    }

    private void RaiseNavigationCompleted(Guid navigationId, Uri requestUri, NavigationCompletedStatus status, Exception? error)
        => SafeRaise(() => NavigationCompleted?.Invoke(this, new NavigationCompletedEventArgs(navigationId, requestUri, status, error)));

    private void SafeRaise(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Keep platform callbacks safe.
        }
    }

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

        if (_detached)
        {
            throw new ObjectDisposedException(nameof(AndroidWebViewAdapter));
        }

        if (!_attached)
        {
            throw new InvalidOperationException("Adapter must be attached before use.");
        }
    }

    private void ThrowIfNotAttachedForCookies()
    {
        if (_detached)
        {
            throw new ObjectDisposedException(nameof(AndroidWebViewAdapter));
        }

        if (!_attached)
        {
            throw new InvalidOperationException("Adapter is not attached.");
        }
    }

    // ==================== Inner classes ====================

    /// <summary>
    /// Custom WebViewClient that delegates navigation events to the adapter.
    /// </summary>
    private sealed class AdapterWebViewClient : WebViewClient
    {
        private readonly AndroidWebViewAdapter _adapter;

        public AdapterWebViewClient(AndroidWebViewAdapter adapter)
        {
            _adapter = adapter;
        }

        public override bool ShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request)
            => _adapter.HandleShouldOverrideUrlLoading(view, request);

        public override WebResourceResponse? ShouldInterceptRequest(AWebView? view, IWebResourceRequest? request)
        {
            var response = _adapter.HandleShouldInterceptRequest(request);
            return response ?? base.ShouldInterceptRequest(view, request);
        }

        public override void OnPageStarted(AWebView? view, string? url, global::Android.Graphics.Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);
            _adapter.OnPageStarted(view, url);
        }

        public override void OnPageFinished(AWebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            _adapter.OnPageFinished(view, url);
        }

        public override void OnReceivedError(AWebView? view, IWebResourceRequest? request, WebResourceError? error)
        {
            base.OnReceivedError(view, request, error);
            _adapter.OnReceivedError(view, request, error);
        }
    }

    /// <summary>
    /// Custom WebChromeClient that handles new window requests.
    /// </summary>
    private sealed class AdapterWebChromeClient : WebChromeClient
    {
        private readonly AndroidWebViewAdapter _adapter;

        public AdapterWebChromeClient(AndroidWebViewAdapter adapter)
        {
            _adapter = adapter;
        }

        public override bool OnCreateWindow(AWebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
        {
            // Try to extract the URL from the hit test result.
            var url = view?.GetHitTestResult()?.Extra;

            _adapter.OnNewWindowRequested(view, url);

            // Return false to indicate we're not providing a new WebView.
            return false;
        }

        public override void OnPermissionRequest(PermissionRequest? request)
        {
            if (request is null || _adapter._detached)
            {
                request?.Deny();
                return;
            }

            var resources = request.GetResources() ?? [];
            foreach (var resource in resources)
            {
                var kind = resource switch
                {
                    PermissionRequest.ResourceVideoCapture => WebViewPermissionKind.Camera,
                    PermissionRequest.ResourceAudioCapture => WebViewPermissionKind.Microphone,
                    PermissionRequest.ResourceMidiSysex => WebViewPermissionKind.Midi,
                    _ => WebViewPermissionKind.Other
                };

                Uri.TryCreate(request.Origin?.ToString(), UriKind.Absolute, out var origin);
                var args = new PermissionRequestedEventArgs(kind, origin);
                _adapter.PermissionRequested?.Invoke(_adapter, args);

                if (args.State == PermissionState.Deny)
                {
                    request.Deny();
                    return;
                }

                if (args.State == PermissionState.Allow)
                {
                    // Check that Android runtime permissions are granted before allowing.
                    if (HasRequiredRuntimePermissions(resources))
                    {
                        request.Grant(resources);
                    }
                    else
                    {
                        // Runtime permission not granted at system level; deny gracefully.
                        // App should request runtime permissions via Activity before allowing WebView access.
                        request.Deny();
                    }
                    return;
                }
            }

            // Default: deny (Android has no built-in "ask user" for WebView permissions)
            base.OnPermissionRequest(request);
        }

        private static bool HasRequiredRuntimePermissions(string[] resources)
        {
            var context = global::Android.App.Application.Context;
            foreach (var resource in resources)
            {
                var manifestPermission = resource switch
                {
                    PermissionRequest.ResourceVideoCapture => global::Android.Manifest.Permission.Camera,
                    PermissionRequest.ResourceAudioCapture => global::Android.Manifest.Permission.RecordAudio,
                    _ => null
                };

                if (manifestPermission is not null)
                {
                    if (global::AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, manifestPermission)
                        != global::Android.Content.PM.Permission.Granted)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }

    /// <summary>
    /// JavaScript interface bridge for receiving WebMessages from page scripts.
    /// </summary>
    private sealed class AndroidJsBridge : JavaObject
    {
        private readonly AndroidWebViewAdapter _adapter;
        private readonly Guid _channelId;

        public AndroidJsBridge(AndroidWebViewAdapter adapter, Guid channelId)
        {
            _adapter = adapter;
            _channelId = channelId;
        }

        [global::Android.Webkit.JavascriptInterface]
        [Export("postMessage")]
        public void PostMessage(string message)
        {
            // The message is a JSON envelope from our bridge script.
            _adapter.OnWebMessageReceived(message, string.Empty);
        }
    }

    /// <summary>
    /// IValueCallback implementation for script execution results.
    /// </summary>
    private sealed class ScriptResultCallback : JavaObject, IValueCallback
    {
        private readonly TaskCompletionSource<string?> _tcs;

        public ScriptResultCallback(TaskCompletionSource<string?> tcs)
        {
            _tcs = tcs;
        }

        public void OnReceiveValue(JavaObject? value)
        {
            var result = value?.ToString();
            _tcs.TrySetResult(ScriptResultHelper.NormalizeJsonResult(result));
        }
    }

    /// <summary>
    /// DownloadListener that forwards download events to the adapter.
    /// </summary>
    private sealed class AdapterDownloadListener : JavaObject, IDownloadListener
    {
        private readonly AndroidWebViewAdapter _adapter;

        public AdapterDownloadListener(AndroidWebViewAdapter adapter)
        {
            _adapter = adapter;
        }

        public void OnDownloadStart(string? url, string? userAgent, string? contentDisposition, string? mimetype, long contentLength)
        {
            if (_adapter._detached || string.IsNullOrEmpty(url)) return;

            var uri = new Uri(url!);
            var fileName = !string.IsNullOrEmpty(contentDisposition)
                ? global::Android.Webkit.URLUtil.GuessFileName(url, contentDisposition, mimetype)
                : System.IO.Path.GetFileName(uri.LocalPath);

            var args = new DownloadRequestedEventArgs(
                uri,
                fileName,
                mimetype,
                contentLength > 0 ? contentLength : null);

            _adapter.DownloadRequested?.Invoke(_adapter, args);

            // If consumer handled or cancelled, nothing more to do.
            if (args.Cancel || args.Handled) return;

            // Default: use Android DownloadManager for unhandled downloads.
            try
            {
                var context = _adapter._webView?.Context;
                if (context is null) return;

                var request = new global::Android.App.DownloadManager.Request(global::Android.Net.Uri.Parse(url));
                request.SetMimeType(mimetype);
                request.AddRequestHeader("User-Agent", userAgent ?? string.Empty);

                if (!string.IsNullOrEmpty(args.DownloadPath))
                {
                    request.SetDestinationUri(global::Android.Net.Uri.FromFile(new Java.IO.File(args.DownloadPath)));
                }
                else
                {
                    request.SetNotificationVisibility(global::Android.App.DownloadVisibility.VisibleNotifyCompleted);
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        request.SetDestinationInExternalPublicDir(
                            global::Android.OS.Environment.DirectoryDownloads, fileName);
                    }
                }

                var downloadManager = (global::Android.App.DownloadManager?)context.GetSystemService(global::Android.Content.Context.DownloadService);
                downloadManager?.Enqueue(request);
            }
            catch
            {
                // Best-effort download; ignore failures.
            }
        }
    }

    /// <summary>
    /// IValueCallback for cookie operations (set/remove).
    /// </summary>
    private sealed class CookieValueCallback : JavaObject, IValueCallback
    {
        private readonly TaskCompletionSource _tcs;

        public CookieValueCallback(TaskCompletionSource tcs)
        {
            _tcs = tcs;
        }

        public void OnReceiveValue(JavaObject? value)
        {
            _tcs.TrySetResult();
        }
    }

    // ==================== ICommandAdapter ====================

    public void ExecuteCommand(WebViewCommand command)
    {
        if (_webView is null) return;
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
            RunOnUiThread(() => _webView?.EvaluateJavascript(jsCommand, null));
    }

    // ==================== IScreenshotAdapter ====================

    public Task<byte[]> CaptureScreenshotAsync()
    {
        if (_webView is null)
            throw new InvalidOperationException("WebView is not initialized.");

        var bitmap = Bitmap.CreateBitmap(_webView.Width, _webView.Height, Bitmap.Config.Argb8888!);
        var canvas = new Canvas(bitmap!);
        _webView.Draw(canvas);

        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        bitmap.Dispose();

        return Task.FromResult(stream.ToArray());
    }

    // ==================== IPrintAdapter ====================

    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options)
    {
        if (_webView is null)
            throw new InvalidOperationException("WebView is not initialized.");

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnUiThread(() =>
        {
            try
            {
                var opts = options ?? new PdfPrintOptions();

                // Convert inches to PostScript points (72 pt = 1 in).
                var pageW = (int)(opts.PageWidth * 72);
                var pageH = (int)(opts.PageHeight * 72);
                if (opts.Landscape) (pageW, pageH) = (pageH, pageW);

                var ml = (int)(opts.MarginLeft * 72);
                var mt = (int)(opts.MarginTop * 72);
                var mr = (int)(opts.MarginRight * 72);
                var mb = (int)(opts.MarginBottom * 72);
                var contentW = pageW - ml - mr;
                var contentH = pageH - mt - mb;

                if (_webView!.Width <= 0)
                {
                    tcs.TrySetException(new InvalidOperationException("WebView has zero width; cannot generate PDF."));
                    return;
                }

                // Scale factor: fit WebView pixel width into PDF content width.
                float scaleX = (float)contentW / _webView.Width;

                // Full content height in PDF points.
                // ContentHeight is in CSS px; multiply by display density to get physical px.
                float density = _webView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
                float webContentPx = _webView.ContentHeight * density;
                int totalPdfH = Math.Max(contentH, (int)(webContentPx * scaleX));

                var pdfDoc = new global::Android.Graphics.Pdf.PdfDocument();
                int pageNum = 1;
                int offset = 0;

                while (offset < totalPdfH)
                {
                    var pi = new global::Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(pageW, pageH, pageNum).Create()!;
                    var page = pdfDoc.StartPage(pi);
                    var canvas = page!.Canvas!;

                    // Clip to content area (respect margins).
                    canvas.ClipRect(ml, mt, pageW - mr, pageH - mb);

                    // Translate for margins and vertical page offset, then scale.
                    canvas.Translate(ml, mt - offset);
                    canvas.Scale(scaleX, scaleX);

                    _webView.Draw(canvas);

                    pdfDoc.FinishPage(page);
                    offset += contentH;
                    pageNum++;
                }

                using var output = new MemoryStream();
                pdfDoc.WriteTo(output);
                pdfDoc.Close();

                tcs.TrySetResult(output.ToArray());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    // ==================== IFindInPageAdapter ====================

    public Task<FindInPageEventArgs> FindAsync(string text, FindInPageOptions? options)
    {
        if (_webView is null)
            throw new InvalidOperationException("WebView is not initialized.");

        var tcs = new TaskCompletionSource<FindInPageEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Android WebView has built-in findAllAsync + FindListener.
        // All WebView methods must be called on the Android UI thread.
        RunOnUiThread(() =>
        {
            _webView?.SetFindListener(new FindListener(tcs));
            _webView?.FindAllAsync(text);
        });

        return tcs.Task;
    }

    public void StopFind(bool clearHighlights = true)
    {
        RunOnUiThread(() => _webView?.ClearMatches());
    }

    private sealed class FindListener : Java.Lang.Object, AWebView.IFindListener
    {
        private readonly TaskCompletionSource<FindInPageEventArgs> _tcs;
        public FindListener(TaskCompletionSource<FindInPageEventArgs> tcs) => _tcs = tcs;

        public void OnFindResultReceived(int activeMatchOrdinal, int numberOfMatches, bool isDoneCounting)
        {
            if (!isDoneCounting) return;
            _tcs.TrySetResult(new FindInPageEventArgs
            {
                ActiveMatchIndex = activeMatchOrdinal,
                TotalMatches = numberOfMatches
            });
        }
    }

    // ==================== IZoomAdapter ====================

    public event EventHandler<double>? ZoomFactorChanged;

    public double ZoomFactor
    {
        get
        {
            if (_webView?.Settings is null) return 1.0;
            return _webView.Settings.TextZoom / 100.0;
        }
        set
        {
            if (_webView?.Settings is null) return;
            var percent = (int)Math.Round(value * 100);
            _webView.Settings.TextZoom = percent;
            ZoomFactorChanged?.Invoke(this, value);
        }
    }

    // ==================== IPreloadScriptAdapter ====================

    private readonly Dictionary<string, string> _preloadScripts = new();
    private int _nextScriptId;

    public string AddPreloadScript(string javaScript)
    {
        if (_webView is null)
            throw new InvalidOperationException("WebView is not initialized.");
        var scriptId = $"preload_{++_nextScriptId}";
        _preloadScripts[scriptId] = javaScript;
        // Android WebView injects scripts in onPageStarted via WebViewClient.
        // The scripts are stored and executed at page load time.
        return scriptId;
    }

    public void RemovePreloadScript(string scriptId)
    {
        _preloadScripts.Remove(scriptId);
    }

    // ==================== IContextMenuAdapter ====================

#pragma warning disable CS0067 // Event is required by IContextMenuAdapter but not yet raised on Android
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
#pragma warning restore CS0067

    // ==================== IDevToolsAdapter ====================

    public void OpenDevTools() { }
    public void CloseDevTools() { }
    public bool IsDevToolsOpen => false;
}
