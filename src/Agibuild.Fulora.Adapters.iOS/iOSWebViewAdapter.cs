using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using ObjCRuntime;

namespace Agibuild.Fulora.Adapters.iOS;

[SupportedOSPlatform("ios")]
internal sealed class iOSWebViewAdapter : IWebViewAdapter, INativeWebViewHandleProvider, ICookieAdapter, IWebViewAdapterOptions,
    ICustomSchemeAdapter, IDownloadAdapter, IPermissionAdapter, ICommandAdapter, IScreenshotAdapter, IPrintAdapter,
    IFindInPageAdapter, IZoomAdapter, IPreloadScriptAdapter, IContextMenuAdapter, IDevToolsAdapter, IDragDropAdapter
{
    private static bool DiagnosticsEnabled
        => string.Equals(Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal);

    private IWebViewAdapterHost? _host;

    private bool _initialized;
    private bool _attached;
    private bool _detached;

    // Native shim state
    private IntPtr _native;
    private GCHandle _selfHandle;
    private NativeMethods.AgWkCallbacks _callbacks;
    private NativeMethods.PolicyRequestCb? _policyCb;
    private NativeMethods.NavigationCompletedCb? _navCompletedCb;
    private NativeMethods.ScriptResultCb? _scriptResultCb;
    private NativeMethods.MessageCb? _messageCb;
    private NativeMethods.DownloadCb? _downloadCb;
    private NativeMethods.PermissionCb? _permissionCb;
    private NativeMethods.SchemeRequestCb? _schemeRequestCb;
    private NativeMethods.DragEnteredCb? _dragEnteredCb;
    private NativeMethods.DragUpdatedCb? _dragUpdatedCb;
    private NativeMethods.DragExitedCb? _dragExitedCb;
    private NativeMethods.DropPerformedCb? _dropPerformedCb;

    // Pinned buffers for scheme responses
    private byte[]? _schemeResponseData;
    private GCHandle _schemeResponsePin;
    private byte[]? _schemeMimeData;
    private GCHandle _schemeMimePin;

    // Lock protects navigation state accessed from both native callbacks
    // (which may run on background threads after an await) and API methods (UI thread).
    private readonly object _navLock = new();

    // Navigation state — guarded by _navLock
    private Guid _activeNavigationId;
    private Uri? _activeRequestUri;
    private bool _activeNavigationCompleted;
    private bool _apiNavigationActive;

    // Native-initiated main-frame correlation state (redirect chain) — guarded by _navLock
    private bool _nativeCorrelationActive;
    private Guid _nativeCorrelationId;

    // Script completion
    private long _nextScriptRequestId;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<string?>> _scriptTcsById = new();

    public bool CanGoBack => _attached && !_detached && NativeMethods.CanGoBack(_native);
    public bool CanGoForward => _attached && !_detached && NativeMethods.CanGoForward(_native);

    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;

    // IDragDropAdapter events
    public event EventHandler<DragEventArgs>? DragEntered;
    public event EventHandler<DragEventArgs>? DragOver;
    public event EventHandler<EventArgs>? DragLeft;
    public event EventHandler<DropEventArgs>? DropCompleted;

    private IReadOnlyList<CustomSchemeRegistration>? _customSchemes;

    public void RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes)
    {
        _customSchemes = schemes;
        if (_native == IntPtr.Zero) return;
        foreach (var scheme in schemes)
        {
            if (!string.IsNullOrEmpty(scheme.SchemeName))
            {
                NativeMethods.RegisterCustomScheme(_native, scheme.SchemeName);
            }
        }
    }

    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested
    {
        add { }
        remove { }
    }

    public void Initialize(IWebViewAdapterHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (_initialized)
        {
            throw new InvalidOperationException($"{nameof(Initialize)} can only be called once.");
        }

        _initialized = true;
        _host = host;

        _selfHandle = GCHandle.Alloc(this);

        // Use pre-created static delegates (AOT-compatible via [MonoPInvokeCallback]).
        _policyCb = PolicyRequestTrampoline;
        _navCompletedCb = NavigationCompletedTrampoline;
        _scriptResultCb = ScriptResultTrampoline;
        _messageCb = MessageTrampoline;
        _downloadCb = DownloadTrampoline;
        unsafe
        {
            _permissionCb = PermissionTrampoline;
            _schemeRequestCb = SchemeRequestTrampoline;
        }
        _dragEnteredCb = DragEnteredTrampoline;
        _dragUpdatedCb = DragUpdatedTrampoline;
        _dragExitedCb = DragExitedTrampoline;
        _dropPerformedCb = DropPerformedTrampoline;

        _callbacks = new NativeMethods.AgWkCallbacks
        {
            on_policy_request = Marshal.GetFunctionPointerForDelegate(_policyCb),
            on_navigation_completed = Marshal.GetFunctionPointerForDelegate(_navCompletedCb),
            on_script_result = Marshal.GetFunctionPointerForDelegate(_scriptResultCb),
            on_message = Marshal.GetFunctionPointerForDelegate(_messageCb),
            on_download = Marshal.GetFunctionPointerForDelegate(_downloadCb),
            on_permission = Marshal.GetFunctionPointerForDelegate(_permissionCb),
            on_scheme_request = Marshal.GetFunctionPointerForDelegate(_schemeRequestCb),
            on_drag_entered = Marshal.GetFunctionPointerForDelegate(_dragEnteredCb),
            on_drag_updated = Marshal.GetFunctionPointerForDelegate(_dragUpdatedCb),
            on_drag_exited = Marshal.GetFunctionPointerForDelegate(_dragExitedCb),
            on_drop_performed = Marshal.GetFunctionPointerForDelegate(_dropPerformedCb),
        };

        _native = NativeMethods.Create(ref _callbacks, GCHandle.ToIntPtr(_selfHandle));
        if (_native == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create native WKWebView shim instance.");
        }
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

        if (!OperatingSystem.IsIOS())
        {
            throw new PlatformNotSupportedException("WKWebView iOS adapter can only be used on iOS.");
        }

        if (!NativeMethods.Attach(_native, parentHandle.Handle))
        {
            throw new InvalidOperationException("Native WKWebView shim failed to attach. Ensure the parent handle is a UIView.");
        }

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
            if (_native != IntPtr.Zero)
            {
                NativeMethods.Detach(_native);
                NativeMethods.Destroy(_native);
            }
        }
        finally
        {
            _native = IntPtr.Zero;

            foreach (var kvp in _scriptTcsById)
            {
                kvp.Value.TrySetException(new ObjectDisposedException(nameof(iOSWebViewAdapter)));
            }
            _scriptTcsById.Clear();

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }

            if (_schemeResponsePin.IsAllocated) _schemeResponsePin.Free();
            if (_schemeMimePin.IsAllocated) _schemeMimePin.Free();

            ClearNavigationState();
        }
    }

    public Task NavigateAsync(Guid navigationId, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ThrowIfNotAttached();

        lock (_navLock) { BeginApiNavigation(navigationId, requestUri: uri); }
        NativeMethods.Navigate(_native, uri.AbsoluteUri);
        return Task.CompletedTask;
    }

    public Task NavigateToStringAsync(Guid navigationId, string html)
        => NavigateToStringAsync(navigationId, html, baseUrl: null);

    public Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ThrowIfNotAttached();

        lock (_navLock) { BeginApiNavigation(navigationId, requestUri: baseUrl ?? new Uri("about:blank")); }
        NativeMethods.LoadHtml(_native, html, baseUrl: baseUrl?.AbsoluteUri);
        return Task.CompletedTask;
    }

    public Task<string?> InvokeScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        ThrowIfNotAttached();

        var requestId = (ulong)Interlocked.Increment(ref _nextScriptRequestId);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _scriptTcsById.TryAdd(requestId, tcs);

        NativeMethods.EvalJs(_native, requestId, script);
        return tcs.Task;
    }

    public bool GoBack(Guid navigationId)
    {
        ThrowIfNotAttached();
        lock (_navLock) { BeginApiNavigation(navigationId, requestUri: _activeRequestUri ?? new Uri("about:blank")); }
        return NativeMethods.GoBack(_native);
    }

    public bool GoForward(Guid navigationId)
    {
        ThrowIfNotAttached();
        lock (_navLock) { BeginApiNavigation(navigationId, requestUri: _activeRequestUri ?? new Uri("about:blank")); }
        return NativeMethods.GoForward(_native);
    }

    public bool Refresh(Guid navigationId)
    {
        ThrowIfNotAttached();
        lock (_navLock) { BeginApiNavigation(navigationId, requestUri: _activeRequestUri ?? new Uri("about:blank")); }
        return NativeMethods.Reload(_native);
    }

    public bool Stop()
    {
        ThrowIfNotAttached();
        NativeMethods.Stop(_native);
        return true;
    }

    public INativeHandle? TryGetWebViewHandle()
    {
        if (!_attached || _detached) return null;

        var ptr = NativeMethods.GetWebViewHandle(_native);
        return ptr == IntPtr.Zero ? null : new AppleWKWebViewPlatformHandle(ptr);
    }

    /// <summary>Typed platform handle for Apple WKWebView (iOS).</summary>
    private sealed record AppleWKWebViewPlatformHandle(nint WKWebViewHandle) : IAppleWKWebViewPlatformHandle
    {
        public nint Handle => WKWebViewHandle;
        public string HandleDescriptor => "WKWebView";
    }

    // ---------- IWebViewAdapterOptions ----------

    public void ApplyEnvironmentOptions(IWebViewEnvironmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfNotInitialized();

        if (_attached)
        {
            throw new InvalidOperationException("Environment options must be applied before Attach.");
        }

        NativeMethods.SetEnableDevTools(_native, options.EnableDevTools);
        NativeMethods.SetEphemeral(_native, options.UseEphemeralSession);

        if (options.CustomUserAgent is not null)
        {
            NativeMethods.SetUserAgent(_native, options.CustomUserAgent);
        }
    }

    public void SetCustomUserAgent(string? userAgent)
    {
        ThrowIfNotInitialized();
        NativeMethods.SetUserAgent(_native, userAgent);
    }

    // ---------- ICookieAdapter ----------

    public Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri)
    {
        ThrowIfNotAttachedForCookies();
        var tcs = new TaskCompletionSource<IReadOnlyList<WebViewCookie>>();
        var tcsHandle = GCHandle.Alloc(tcs);

        NativeMethods.CookiesGet(_native, uri.AbsoluteUri, CookiesGetTrampoline, GCHandle.ToIntPtr(tcsHandle));
        return tcs.Task;
    }

    public Task SetCookieAsync(WebViewCookie cookie)
    {
        ThrowIfNotAttachedForCookies();
        var tcs = new TaskCompletionSource();
        var tcsHandle = GCHandle.Alloc(tcs);
        var expiresUnix = cookie.Expires.HasValue ? cookie.Expires.Value.ToUnixTimeSeconds() : -1.0;

        NativeMethods.CookieSet(_native,
            cookie.Name, cookie.Value, cookie.Domain, cookie.Path,
            expiresUnix, cookie.IsSecure, cookie.IsHttpOnly,
            CookieOpTrampoline, GCHandle.ToIntPtr(tcsHandle));
        return tcs.Task;
    }

    public Task DeleteCookieAsync(WebViewCookie cookie)
    {
        ThrowIfNotAttachedForCookies();
        var tcs = new TaskCompletionSource();
        var tcsHandle = GCHandle.Alloc(tcs);

        NativeMethods.CookieDelete(_native,
            cookie.Name, cookie.Domain, cookie.Path,
            CookieOpTrampoline, GCHandle.ToIntPtr(tcsHandle));
        return tcs.Task;
    }

    public Task ClearAllCookiesAsync()
    {
        ThrowIfNotAttachedForCookies();
        var tcs = new TaskCompletionSource();
        var tcsHandle = GCHandle.Alloc(tcs);

        NativeMethods.CookiesClearAll(_native, CookieOpTrampoline, GCHandle.ToIntPtr(tcsHandle));
        return tcs.Task;
    }

    private void ThrowIfNotAttachedForCookies()
    {
        ObjectDisposedException.ThrowIf(_detached, nameof(iOSWebViewAdapter));
        if (!_attached)
            throw new InvalidOperationException("Adapter is not attached.");
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

    private void RaiseNavigationCompleted(Guid navigationId, Uri requestUri, NavigationCompletedStatus status, Exception? error)
        => SafeRaise(() => NavigationCompleted?.Invoke(this, new NavigationCompletedEventArgs(navigationId, requestUri, status, error)));

    private void RaiseWebMessageReceived(string body, string origin, Guid channelId, int protocolVersion)
        => SafeRaise(() => WebMessageReceived?.Invoke(this, new WebMessageReceivedEventArgs(body, origin, channelId, protocolVersion)));

    private void CompleteCanceledFromPolicyDecision(Guid navigationId, Uri requestUri)
    {
        if (_detached) return;

        lock (_navLock)
        {
            if (_activeNavigationId == navigationId && _activeNavigationCompleted)
                return;

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
        if (_detached) return;

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

    private void ClearNativeCorrelationIfNeeded()
    {
        _nativeCorrelationActive = false;
        _nativeCorrelationId = Guid.Empty;
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

        if (navigationType < 0)
        {
            navigationType = 5;
        }

        // LinkActivated=0, FormSubmitted=1, BackForward=2, Reload=3, FormResubmitted=4, Other=5
        if (navigationType is 0 or 1 or 4)
        {
            _nativeCorrelationId = Guid.NewGuid();
            return _nativeCorrelationId;
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

        ObjectDisposedException.ThrowIf(_detached, nameof(iOSWebViewAdapter));

        if (!_attached || _native == IntPtr.Zero)
        {
            throw new InvalidOperationException("Adapter must be attached before use.");
        }
    }

    private static void SafeRaise(Action action)
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

    // ==== AOT-safe static trampolines for native callbacks ====
    // iOS does not allow JIT. All delegates passed to native code must be
    // static methods with [MonoPInvokeCallback] so the AOT compiler can
    // generate the native-to-managed wrappers at build time.

    [MonoPInvokeCallback(typeof(NativeMethods.PolicyRequestCb))]
    private static void PolicyRequestTrampoline(IntPtr userData, ulong requestId, IntPtr urlUtf8, byte isMainFrame, byte isNewWindow, int navigationType)
    {
        var self = NativeMethods.FromUserData(userData);
        self?.OnPolicyRequest(requestId, NativeMethods.PtrToString(urlUtf8), isMainFrame != 0, isNewWindow != 0, navigationType);
    }

    [MonoPInvokeCallback(typeof(NativeMethods.NavigationCompletedCb))]
    private static void NavigationCompletedTrampoline(IntPtr userData, IntPtr urlUtf8, int status, long errorCode, IntPtr errorMessageUtf8)
    {
        var self = NativeMethods.FromUserData(userData);
        self?.OnNavigationCompletedNative(NativeMethods.PtrToString(urlUtf8), status, errorCode, NativeMethods.PtrToString(errorMessageUtf8));
    }

    [MonoPInvokeCallback(typeof(NativeMethods.ScriptResultCb))]
    private static void ScriptResultTrampoline(IntPtr userData, ulong requestId, IntPtr resultUtf8, IntPtr errorMessageUtf8)
    {
        var self = NativeMethods.FromUserData(userData);
        self?.OnScriptResultNative(requestId, NativeMethods.PtrToStringNullable(resultUtf8), NativeMethods.PtrToStringNullable(errorMessageUtf8));
    }

    [MonoPInvokeCallback(typeof(NativeMethods.MessageCb))]
    private static void MessageTrampoline(IntPtr userData, IntPtr bodyUtf8, IntPtr originUtf8)
    {
        var self = NativeMethods.FromUserData(userData);
        self?.OnMessageNative(NativeMethods.PtrToString(bodyUtf8), NativeMethods.PtrToString(originUtf8));
    }

    [MonoPInvokeCallback(typeof(NativeMethods.DownloadCb))]
    private static void DownloadTrampoline(IntPtr userData, IntPtr urlUtf8, IntPtr suggestedFileNameUtf8, IntPtr mimeTypeUtf8, long contentLength)
    {
        var self = NativeMethods.FromUserData(userData);
        self?.OnDownloadNative(
            NativeMethods.PtrToString(urlUtf8),
            NativeMethods.PtrToString(suggestedFileNameUtf8),
            NativeMethods.PtrToString(mimeTypeUtf8),
            contentLength);
    }

    [MonoPInvokeCallback(typeof(NativeMethods.PermissionCb))]
    private static unsafe void PermissionTrampoline(IntPtr userData, int permissionKind, IntPtr originUtf8, int* outState)
    {
        var self = NativeMethods.FromUserData(userData);
        if (self is not null)
        {
            *outState = self.OnPermissionNative(permissionKind, NativeMethods.PtrToString(originUtf8));
        }
    }

    [MonoPInvokeCallback(typeof(NativeMethods.SchemeRequestCb))]
    private static unsafe bool SchemeRequestTrampoline(
        IntPtr userData, IntPtr urlUtf8, IntPtr methodUtf8,
        IntPtr* outResponseData, long* outResponseLength, IntPtr* outMimeTypeUtf8, int* outStatusCode)
    {
        var self = NativeMethods.FromUserData(userData);
        if (self is null) return false;
        return self.OnSchemeRequestNative(
            NativeMethods.PtrToString(urlUtf8),
            NativeMethods.PtrToString(methodUtf8),
            outResponseData, outResponseLength, outMimeTypeUtf8, outStatusCode);
    }

    // ==================== IDragDropAdapter trampolines ====================

    [MonoPInvokeCallback(typeof(NativeMethods.DragEnteredCb))]
    private static void DragEnteredTrampoline(IntPtr userData, IntPtr filesJsonUtf8, IntPtr textUtf8, IntPtr htmlUtf8, IntPtr uriUtf8, double x, double y)
    {
        var self = NativeMethods.FromUserData(userData);
        if (self is null) return;
        var payload = ParseDragPayload(filesJsonUtf8, textUtf8, htmlUtf8, uriUtf8);
        self.DragEntered?.Invoke(self, new DragEventArgs
        {
            Payload = payload,
            AllowedEffects = DragDropEffects.Copy,
            Effect = DragDropEffects.Copy,
            X = x,
            Y = y
        });
    }

    [MonoPInvokeCallback(typeof(NativeMethods.DragUpdatedCb))]
    private static void DragUpdatedTrampoline(IntPtr userData, double x, double y)
    {
        var self = NativeMethods.FromUserData(userData);
        if (self is null) return;
        self.DragOver?.Invoke(self, new DragEventArgs
        {
            Payload = new DragDropPayload(),
            AllowedEffects = DragDropEffects.Copy,
            Effect = DragDropEffects.Copy,
            X = x,
            Y = y
        });
    }

    [MonoPInvokeCallback(typeof(NativeMethods.DragExitedCb))]
    private static void DragExitedTrampoline(IntPtr userData)
    {
        var self = NativeMethods.FromUserData(userData);
        self?.DragLeft?.Invoke(self, EventArgs.Empty);
    }

    [MonoPInvokeCallback(typeof(NativeMethods.DropPerformedCb))]
    private static void DropPerformedTrampoline(IntPtr userData, IntPtr filesJsonUtf8, IntPtr textUtf8, IntPtr htmlUtf8, IntPtr uriUtf8, double x, double y)
    {
        var self = NativeMethods.FromUserData(userData);
        if (self is null) return;
        var payload = ParseDragPayload(filesJsonUtf8, textUtf8, htmlUtf8, uriUtf8);
        self.DropCompleted?.Invoke(self, new DropEventArgs
        {
            Payload = payload,
            Effect = DragDropEffects.Copy,
            X = x,
            Y = y
        });
    }

    private static DragDropPayload ParseDragPayload(IntPtr filesJsonUtf8, IntPtr textUtf8, IntPtr htmlUtf8, IntPtr uriUtf8)
    {
        var payload = new DragDropPayload
        {
            Text = NativeMethods.PtrToStringNullable(textUtf8),
            Html = NativeMethods.PtrToStringNullable(htmlUtf8),
            Uri = NativeMethods.PtrToStringNullable(uriUtf8)
        };

        var filesJson = NativeMethods.PtrToStringNullable(filesJsonUtf8);
        if (!string.IsNullOrEmpty(filesJson) && filesJson != "[]")
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(filesJson);
                var files = new List<FileDropInfo>();
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    var path = elem.GetProperty("path").GetString() ?? "";
                    long? size = elem.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == System.Text.Json.JsonValueKind.Number
                        ? sizeProp.GetInt64() : null;
                    files.Add(new FileDropInfo(path, null, size));
                }
                payload = payload with { Files = files };
            }
            catch { /* malformed JSON — ignore */ }
        }
        return payload;
    }

    [MonoPInvokeCallback(typeof(NativeMethods.CookiesGetCb))]
    private static void CookiesGetTrampoline(IntPtr context, IntPtr jsonUtf8)
    {
        var h = GCHandle.FromIntPtr(context);
        var t = (TaskCompletionSource<IReadOnlyList<WebViewCookie>>)h.Target!;
        h.Free();

        try
        {
            var json = NativeMethods.PtrToString(jsonUtf8);
            var cookies = AdapterCookieParser.ParseCookiesJson(json);
            t.TrySetResult(cookies);
        }
        catch (Exception ex)
        {
            t.TrySetException(ex);
        }
    }

    [MonoPInvokeCallback(typeof(NativeMethods.CookieOpCb))]
    private static void CookieOpTrampoline(IntPtr context, bool success, IntPtr errorUtf8)
    {
        var h = GCHandle.FromIntPtr(context);
        var t = (TaskCompletionSource)h.Target!;
        h.Free();

        if (success)
            t.TrySetResult();
        else
            t.TrySetException(new InvalidOperationException(NativeMethods.PtrToString(errorUtf8)));
    }

    // ==== Native callbacks (called from native shim) ====

    private void OnPolicyRequest(ulong requestId, string? url, bool isMainFrame, bool isNewWindow, int navigationType)
    {
        _ = DecidePolicyAsync(requestId, url, isMainFrame, isNewWindow, navigationType);
    }

    private async Task DecidePolicyAsync(ulong requestId, string? url, bool isMainFrame, bool isNewWindow, int navigationType)
    {
        try
        {
            bool apiActive;
            lock (_navLock) { apiActive = _apiNavigationActive; }

            if (DiagnosticsEnabled)
            {
                Console.WriteLine($"[Agibuild.WebView] PolicyRequest id={requestId} main={isMainFrame} newWin={isNewWindow} api={apiActive} url='{url ?? "<null>"}' navType={navigationType}");
            }

            if (_detached)
            {
                NativeMethods.PolicyDecide(_native, requestId, allow: false);
                return;
            }

            if (isNewWindow)
            {
                Uri? newWindowUri = null;
                if (url is not null) Uri.TryCreate(url, UriKind.Absolute, out newWindowUri);
                SafeRaise(() => NewWindowRequested?.Invoke(this, new NewWindowRequestedEventArgs(newWindowUri)));
                NativeMethods.PolicyDecide(_native, requestId, allow: false);
                return;
            }

            if (!isMainFrame)
            {
                NativeMethods.PolicyDecide(_native, requestId, allow: true);
                return;
            }

            if (apiActive)
            {
                NativeMethods.PolicyDecide(_native, requestId, allow: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var requestUri))
            {
                NativeMethods.PolicyDecide(_native, requestId, allow: true);
                return;
            }

            var host = _host;
            if (host is null)
            {
                NativeMethods.PolicyDecide(_native, requestId, allow: false);
                return;
            }

            Guid correlationId;
            NativeNavigationStartingInfo info;
            lock (_navLock)
            {
                var continuation = _nativeCorrelationActive && _activeNavigationId != Guid.Empty && !_activeNavigationCompleted;
                correlationId = GetOrCreateNativeCorrelationId(navigationType, continuation);
                info = new NativeNavigationStartingInfo(correlationId, requestUri, IsMainFrame: true);
            }

            var decision = await host.OnNativeNavigationStartingAsync(info).ConfigureAwait(false);

            if (!decision.IsAllowed)
            {
                if (decision.NavigationId != Guid.Empty)
                {
                    CompleteCanceledFromPolicyDecision(decision.NavigationId, requestUri);
                }

                NativeMethods.PolicyDecide(_native, requestId, allow: false);
                return;
            }

            if (decision.NavigationId == Guid.Empty)
            {
                NativeMethods.PolicyDecide(_native, requestId, allow: false);
                return;
            }

            lock (_navLock)
            {
                _activeNavigationId = decision.NavigationId;
                _activeRequestUri = requestUri;
                _activeNavigationCompleted = false;
            }

            NativeMethods.PolicyDecide(_native, requestId, allow: true);
        }
        catch
        {
            NativeMethods.PolicyDecide(_native, requestId, allow: false);
        }
    }

    private void OnNavigationCompletedNative(string? url, int status, long errorCode, string? errorMessage)
    {
        var requestUri = (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            ? parsed
            : null;

        if (status == 0)
        {
            OnNavigationTerminal(NavigationCompletedStatus.Success, error: null, requestUriOverride: requestUri);
            return;
        }

        if (status == 2)
        {
            OnNavigationTerminal(NavigationCompletedStatus.Canceled, error: null, requestUriOverride: requestUri);
            return;
        }

        var msg = string.IsNullOrWhiteSpace(errorMessage) ? $"Navigation failed (code={errorCode})." : errorMessage!;
        var navId = _activeNavigationId;
        var navUri = requestUri ?? _activeRequestUri ?? new Uri("about:blank");

        var category = status switch
        {
            3 => NavigationErrorCategory.Timeout,
            4 => NavigationErrorCategory.Network,
            5 => NavigationErrorCategory.Ssl,
            _ => NavigationErrorCategory.Other,
        };
        Exception error = NavigationErrorFactory.Create(category, msg, navId, navUri);

        OnNavigationTerminal(NavigationCompletedStatus.Failure, error: error, requestUriOverride: requestUri);
    }

    private void OnScriptResultNative(ulong requestId, string? result, string? errorMessage)
    {
        if (!_scriptTcsById.TryRemove(requestId, out var tcs))
        {
            return;
        }

        if (_detached)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(iOSWebViewAdapter)));
            return;
        }

        if (!string.IsNullOrEmpty(errorMessage))
        {
            tcs.TrySetException(new WebViewScriptException(errorMessage));
            return;
        }

        tcs.TrySetResult(result);
    }

    private void OnMessageNative(string? body, string? origin)
    {
        if (_detached)
        {
            return;
        }

        var channelId = _host?.ChannelId ?? Guid.Empty;
        RaiseWebMessageReceived(body ?? string.Empty, origin ?? string.Empty, channelId, protocolVersion: 1);
    }

    private void OnDownloadNative(string? url, string? suggestedFileName, string? mimeType, long contentLength)
    {
        if (_detached) return;

        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        var args = new DownloadRequestedEventArgs(
            uri,
            string.IsNullOrEmpty(suggestedFileName) ? null : suggestedFileName,
            string.IsNullOrEmpty(mimeType) ? null : mimeType,
            contentLength > 0 ? contentLength : null);

        DownloadRequested?.Invoke(this, args);
    }

    private int OnPermissionNative(int permissionKind, string? origin)
    {
        if (_detached) return 0;

        var kind = permissionKind switch
        {
            1 => WebViewPermissionKind.Camera,
            2 => WebViewPermissionKind.Microphone,
            _ => WebViewPermissionKind.Unknown
        };

        Uri.TryCreate(origin, UriKind.Absolute, out var originUri);
        var args = new PermissionRequestedEventArgs(kind, originUri);
        PermissionRequested?.Invoke(this, args);

        return args.State switch
        {
            PermissionState.Allow => 1,
            PermissionState.Deny => 2,
            _ => 0
        };
    }

    private unsafe bool OnSchemeRequestNative(
        string? url, string? method,
        IntPtr* outResponseData, long* outResponseLength, IntPtr* outMimeTypeUtf8, int* outStatusCode)
    {
        if (_detached) return false;
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var args = new WebResourceRequestedEventArgs(uri, method ?? "GET");
        WebResourceRequested?.Invoke(this, args);

        if (!args.Handled || args.ResponseBody is null)
            return false;

        if (_schemeResponsePin.IsAllocated) _schemeResponsePin.Free();
        if (_schemeMimePin.IsAllocated) _schemeMimePin.Free();

        using var ms = new MemoryStream();
        args.ResponseBody.CopyTo(ms);
        _schemeResponseData = ms.ToArray();
        _schemeResponsePin = GCHandle.Alloc(_schemeResponseData, GCHandleType.Pinned);

        *outResponseData = _schemeResponsePin.AddrOfPinnedObject();
        *outResponseLength = _schemeResponseData.Length;
        *outStatusCode = args.ResponseStatusCode > 0 ? args.ResponseStatusCode : 200;

        var mime = args.ResponseContentType ?? "application/octet-stream";
        _schemeMimeData = System.Text.Encoding.UTF8.GetBytes(mime + '\0');
        _schemeMimePin = GCHandle.Alloc(_schemeMimeData, GCHandleType.Pinned);
        *outMimeTypeUtf8 = _schemeMimePin.AddrOfPinnedObject();

        return true;
    }

    // ==== Native interop ====
    // On iOS, the native shim is statically linked; use DllImport("__Internal").

    private static class NativeMethods
    {
        internal static iOSWebViewAdapter? FromUserData(IntPtr userData)
        {
            if (userData == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var handle = GCHandle.FromIntPtr(userData);
                return handle.Target as iOSWebViewAdapter;
            }
            catch
            {
                return null;
            }
        }

        internal static string PtrToString(IntPtr ptr)
            => ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;

        internal static string? PtrToStringNullable(IntPtr ptr)
            => ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void PolicyRequestCb(IntPtr userData, ulong requestId, IntPtr urlUtf8, byte isMainFrame, byte isNewWindow, int navigationType);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void NavigationCompletedCb(IntPtr userData, IntPtr urlUtf8, int status, long errorCode, IntPtr errorMessageUtf8);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ScriptResultCb(IntPtr userData, ulong requestId, IntPtr resultUtf8, IntPtr errorMessageUtf8);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void MessageCb(IntPtr userData, IntPtr bodyUtf8, IntPtr originUtf8);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DownloadCb(IntPtr userData, IntPtr urlUtf8, IntPtr suggestedFileNameUtf8, IntPtr mimeTypeUtf8, long contentLength);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal unsafe delegate void PermissionCb(IntPtr userData, int permissionKind, IntPtr originUtf8, int* outState);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal unsafe delegate bool SchemeRequestCb(
            IntPtr userData,
            IntPtr urlUtf8,
            IntPtr methodUtf8,
            IntPtr* outResponseData,
            long* outResponseLength,
            IntPtr* outMimeTypeUtf8,
            int* outStatusCode);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DragEnteredCb(IntPtr userData, IntPtr filesJsonUtf8, IntPtr textUtf8, IntPtr htmlUtf8, IntPtr uriUtf8, double x, double y);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DragUpdatedCb(IntPtr userData, double x, double y);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DragExitedCb(IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DropPerformedCb(IntPtr userData, IntPtr filesJsonUtf8, IntPtr textUtf8, IntPtr htmlUtf8, IntPtr uriUtf8, double x, double y);

        [StructLayout(LayoutKind.Sequential)]
        internal struct AgWkCallbacks
        {
            public IntPtr on_policy_request;
            public IntPtr on_navigation_completed;
            public IntPtr on_script_result;
            public IntPtr on_message;
            public IntPtr on_download;
            public IntPtr on_permission;
            public IntPtr on_scheme_request;
            public IntPtr on_drag_entered;
            public IntPtr on_drag_updated;
            public IntPtr on_drag_exited;
            public IntPtr on_drop_performed;
        }

        // Cookie management
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void CookiesGetCb(IntPtr context, IntPtr jsonUtf8);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void CookieOpCb(IntPtr context, [MarshalAs(UnmanagedType.I1)] bool success, IntPtr errorUtf8);

        [DllImport("__Internal", EntryPoint = "ag_wk_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Create(ref AgWkCallbacks callbacks, IntPtr userData);

        [DllImport("__Internal", EntryPoint = "ag_wk_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_register_custom_scheme", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void RegisterCustomScheme(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string schemeUtf8);

        [DllImport("__Internal", EntryPoint = "ag_wk_attach", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool Attach(IntPtr handle, IntPtr uiViewPtr);

        [DllImport("__Internal", EntryPoint = "ag_wk_detach", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Detach(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_policy_decide", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PolicyDecide(IntPtr handle, ulong requestId, [MarshalAs(UnmanagedType.I1)] bool allow);

        [DllImport("__Internal", EntryPoint = "ag_wk_navigate", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Navigate(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);

        [DllImport("__Internal", EntryPoint = "ag_wk_load_html", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void LoadHtml(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string html, [MarshalAs(UnmanagedType.LPUTF8Str)] string? baseUrl);

        [DllImport("__Internal", EntryPoint = "ag_wk_eval_js", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void EvalJs(IntPtr handle, ulong requestId, [MarshalAs(UnmanagedType.LPUTF8Str)] string script);

        [DllImport("__Internal", EntryPoint = "ag_wk_go_back", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool GoBack(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_go_forward", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool GoForward(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_reload", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool Reload(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_stop", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Stop(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_can_go_back", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool CanGoBack(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_can_go_forward", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool CanGoForward(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_get_webview_handle", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetWebViewHandle(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_cookies_get", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CookiesGet(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string url, CookiesGetCb callback, IntPtr context);

        [DllImport("__Internal", EntryPoint = "ag_wk_cookie_set", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CookieSet(IntPtr handle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string domain,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            double expiresUnix, [MarshalAs(UnmanagedType.I1)] bool isSecure, [MarshalAs(UnmanagedType.I1)] bool isHttpOnly,
            CookieOpCb callback, IntPtr context);

        [DllImport("__Internal", EntryPoint = "ag_wk_cookie_delete", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CookieDelete(IntPtr handle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string domain,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            CookieOpCb callback, IntPtr context);

        [DllImport("__Internal", EntryPoint = "ag_wk_cookies_clear_all", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CookiesClearAll(IntPtr handle, CookieOpCb callback, IntPtr context);

        [DllImport("__Internal", EntryPoint = "ag_wk_set_enable_dev_tools", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetEnableDevTools(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool enable);

        [DllImport("__Internal", EntryPoint = "ag_wk_set_ephemeral", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetEphemeral(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool ephemeral);

        [DllImport("__Internal", EntryPoint = "ag_wk_set_user_agent", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetUserAgent(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string? userAgent);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ScreenshotCb(IntPtr context, IntPtr pngData, uint pngLen);

        [DllImport("__Internal", EntryPoint = "ag_wk_capture_screenshot", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CaptureScreenshot(IntPtr handle, ScreenshotCb callback, IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void PdfCb(IntPtr context, IntPtr pdfData, uint pdfLen);

        [DllImport("__Internal", EntryPoint = "ag_wk_print_to_pdf", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PrintToPdf(IntPtr handle, PdfCb callback, IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void FindCb(IntPtr context, int activeMatchIndex, int totalMatches);

        [DllImport("__Internal", EntryPoint = "ag_wk_find_text", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FindText(IntPtr handle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            [MarshalAs(UnmanagedType.I1)] bool caseSensitive,
            [MarshalAs(UnmanagedType.I1)] bool forward,
            FindCb callback, IntPtr context);

        [DllImport("__Internal", EntryPoint = "ag_wk_stop_find", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void StopFind(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_get_zoom", CallingConvention = CallingConvention.Cdecl)]
        internal static extern double GetZoom(IntPtr handle);

        [DllImport("__Internal", EntryPoint = "ag_wk_set_zoom", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetZoom(IntPtr handle, double zoomFactor);

        [DllImport("__Internal", EntryPoint = "ag_wk_add_user_script", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr AddUserScript(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string js);

        [DllImport("__Internal", EntryPoint = "ag_wk_remove_all_user_scripts", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void RemoveAllUserScripts(IntPtr handle);
    }

    // ==================== ICommandAdapter ====================

    public void ExecuteCommand(WebViewCommand command)
    {
        if (_native == IntPtr.Zero) return;
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
            NativeMethods.EvalJs(_native, 0, jsCommand);
    }

    // ==================== IScreenshotAdapter ====================

    public Task<byte[]> CaptureScreenshotAsync()
    {
        ThrowIfNotAttached();
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);

        NativeMethods.CaptureScreenshot(_native, OnScreenshotComplete, GCHandle.ToIntPtr(handle));
        return tcs.Task;
    }

    [MonoPInvokeCallback(typeof(NativeMethods.ScreenshotCb))]
    private static void OnScreenshotComplete(IntPtr context, IntPtr pngData, uint pngLen)
    {
        var handle = GCHandle.FromIntPtr(context);
        var tcs = (TaskCompletionSource<byte[]>)handle.Target!;
        handle.Free();

        if (pngData == IntPtr.Zero || pngLen == 0)
        {
            tcs.TrySetException(new InvalidOperationException("Screenshot capture failed."));
            return;
        }

        var buffer = new byte[pngLen];
        Marshal.Copy(pngData, buffer, 0, (int)pngLen);
        tcs.TrySetResult(buffer);
    }

    // ==================== IPrintAdapter ====================

    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options)
    {
        ThrowIfNotAttached();
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);

        NativeMethods.PrintToPdf(_native, OnPdfComplete, GCHandle.ToIntPtr(handle));
        return tcs.Task;
    }

    [MonoPInvokeCallback(typeof(NativeMethods.PdfCb))]
    private static void OnPdfComplete(IntPtr context, IntPtr pdfData, uint pdfLen)
    {
        var handle = GCHandle.FromIntPtr(context);
        var tcs = (TaskCompletionSource<byte[]>)handle.Target!;
        handle.Free();

        if (pdfData == IntPtr.Zero || pdfLen == 0)
        {
            tcs.TrySetException(new InvalidOperationException("PDF printing failed."));
            return;
        }

        var buffer = new byte[pdfLen];
        Marshal.Copy(pdfData, buffer, 0, (int)pdfLen);
        tcs.TrySetResult(buffer);
    }

    // ==================== IFindInPageAdapter ====================

    public Task<FindInPageEventArgs> FindAsync(string text, FindInPageOptions? options)
    {
        ThrowIfNotAttached();
        var tcs = new TaskCompletionSource<FindInPageEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);

        var caseSensitive = options?.CaseSensitive ?? false;
        var forward = options?.Forward ?? true;

        NativeMethods.FindText(_native, text, caseSensitive, forward, OnFindComplete, GCHandle.ToIntPtr(handle));
        return tcs.Task;
    }

    [MonoPInvokeCallback(typeof(NativeMethods.FindCb))]
    private static void OnFindComplete(IntPtr context, int activeMatchIndex, int totalMatches)
    {
        var handle = GCHandle.FromIntPtr(context);
        var tcs = (TaskCompletionSource<FindInPageEventArgs>)handle.Target!;
        handle.Free();

        tcs.TrySetResult(new FindInPageEventArgs
        {
            ActiveMatchIndex = activeMatchIndex,
            TotalMatches = totalMatches
        });
    }

    public void StopFind(bool clearHighlights = true)
    {
        if (_native == IntPtr.Zero || _detached) return;
        NativeMethods.StopFind(_native);
    }

    // ==================== IZoomAdapter ====================

    public event EventHandler<double>? ZoomFactorChanged;

    private double _zoomFactor = 1.0;

    public double ZoomFactor
    {
        get => _zoomFactor;
        set
        {
            if (_native == IntPtr.Zero || _detached) return;
            _zoomFactor = value;
            NativeMethods.SetZoom(_native, value);
            ZoomFactorChanged?.Invoke(this, value);
        }
    }

    // ==================== IPreloadScriptAdapter ====================

    private readonly Dictionary<string, string> _preloadScripts = new();

    public string AddPreloadScript(string javaScript)
    {
        ThrowIfNotAttached();
        var ptr = NativeMethods.AddUserScript(_native, javaScript);
        if (ptr == IntPtr.Zero)
            throw new InvalidOperationException("Failed to add user script.");
        var scriptId = Marshal.PtrToStringUTF8(ptr)!;
        Marshal.FreeHGlobal(ptr);
        _preloadScripts[scriptId] = javaScript;
        return scriptId;
    }

    public void RemovePreloadScript(string scriptId)
    {
        if (_native == IntPtr.Zero || _detached) return;
        if (_preloadScripts.Remove(scriptId))
        {
            NativeMethods.RemoveAllUserScripts(_native);
            foreach (var remaining in _preloadScripts.Values)
            {
                NativeMethods.AddUserScript(_native, remaining);
            }
        }
    }

    // ==================== IContextMenuAdapter ====================

    // iOS WKWebView context-menu interception is not wired in this adapter yet.
    // Keep no-op accessors to satisfy IContextMenuAdapter without triggering unused-event warnings.
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested
    {
        add { }
        remove { }
    }

    // ==================== IDevToolsAdapter ====================

    public void OpenDevTools() { }
    public void CloseDevTools() { }
    public bool IsDevToolsOpen => false;
}
