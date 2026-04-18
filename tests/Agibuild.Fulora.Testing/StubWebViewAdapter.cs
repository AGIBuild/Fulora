using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora.Testing;

/// <summary>
/// Minimal <see cref="IWebViewAdapter"/> scaffold for unit-test stubs.
/// <para>
/// Unlike <see cref="MockWebViewAdapter"/>, this type carries no observable
/// state or helpers — it is a pure contract satisfier. Every core method,
/// event and navigation primitive is <c>virtual</c> so derived stubs can
/// override exactly the surface they want to exercise; every mandatory
/// capability facet (<see cref="ICookieAdapter"/>,
/// <see cref="ICommandAdapter"/>, <see cref="IZoomAdapter"/>, etc.) is
/// implemented explicitly as a no-op default, so derived stubs do <em>not</em>
/// need to re-declare those interfaces unless they want observable behavior.
/// </para>
/// <para>
/// A stub that needs observable behavior for a capability re-declares the
/// relevant sub-interface and provides a public member — ordinary
/// interface-mapping wins, so the derived implementation takes precedence
/// over the base's explicit default.
/// </para>
/// </summary>
internal abstract class StubWebViewAdapter : IWebViewAdapter
{
    // -----------------------------------------------------------------------
    // Core lifecycle / navigation / script primitives — virtual so derived
    // stubs can override when a test needs observable behavior.
    // -----------------------------------------------------------------------

    public virtual void Initialize(IWebViewAdapterHost host) { }

    public virtual void Attach(INativeHandle parentHandle) { }

    public virtual void Detach() { }

    public virtual Task NavigateAsync(Guid navigationId, Uri uri) => Task.CompletedTask;

    public virtual Task NavigateToStringAsync(Guid navigationId, string html) => Task.CompletedTask;

    public virtual Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl) => Task.CompletedTask;

    public virtual Task<string?> InvokeScriptAsync(string script) => Task.FromResult<string?>(null);

    public virtual bool CanGoBack { get; set; }

    public virtual bool CanGoForward { get; set; }

    public virtual bool GoBack(Guid navigationId) => false;

    public virtual bool GoForward(Guid navigationId) => false;

    public virtual bool Refresh(Guid navigationId) => false;

    public virtual bool Stop() => false;

    // -----------------------------------------------------------------------
    // Core adapter events — public so derived stubs (and test fixtures) can
    // subscribe directly, with protected raisers for ergonomic simulation.
    // -----------------------------------------------------------------------

#pragma warning disable CS0067 // Events raised only by protected helpers; not all tests exercise every channel.
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;

    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;

    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;

    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;
#pragma warning restore CS0067

    public void RaiseNavigationCompleted(NavigationCompletedEventArgs args)
        => NavigationCompleted?.Invoke(this, args);

    public void RaiseNewWindowRequested(NewWindowRequestedEventArgs args)
        => NewWindowRequested?.Invoke(this, args);

    public void RaiseWebMessageReceived(WebMessageReceivedEventArgs args)
        => WebMessageReceived?.Invoke(this, args);

    public void RaiseWebResourceRequested(WebResourceRequestedEventArgs args)
        => WebResourceRequested?.Invoke(this, args);

    public void RaiseEnvironmentRequested(EnvironmentRequestedEventArgs args)
        => EnvironmentRequested?.Invoke(this, args);

    // -----------------------------------------------------------------------
    // Explicit mandatory-capability defaults. Derived stubs that re-declare a
    // facet interface and provide a public member take over the mapping via
    // ordinary interface-dispatch rules (no `new`/`override` needed).
    // -----------------------------------------------------------------------

    INativeHandle? INativeWebViewHandleProvider.TryGetWebViewHandle() => null;

    void IWebViewAdapterOptions.ApplyEnvironmentOptions(IWebViewEnvironmentOptions options) { }

    void IWebViewAdapterOptions.SetCustomUserAgent(string? userAgent) { }

    Task<IReadOnlyList<WebViewCookie>> ICookieAdapter.GetCookiesAsync(Uri uri)
        => Task.FromResult<IReadOnlyList<WebViewCookie>>([]);

    Task ICookieAdapter.SetCookieAsync(WebViewCookie cookie) => Task.CompletedTask;

    Task ICookieAdapter.DeleteCookieAsync(WebViewCookie cookie) => Task.CompletedTask;

    Task ICookieAdapter.ClearAllCookiesAsync() => Task.CompletedTask;

    void ICommandAdapter.ExecuteCommand(WebViewCommand command) { }

    void ICustomSchemeAdapter.RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes) { }

    event EventHandler<DownloadRequestedEventArgs>? IDownloadAdapter.DownloadRequested
    {
        add { }
        remove { }
    }

    event EventHandler<PermissionRequestedEventArgs>? IPermissionAdapter.PermissionRequested
    {
        add { }
        remove { }
    }

    Task<byte[]> IScreenshotAdapter.CaptureScreenshotAsync()
        => Task.FromResult(Array.Empty<byte>());

    Task<byte[]> IPrintAdapter.PrintToPdfAsync(PdfPrintOptions? options)
        => Task.FromResult(Array.Empty<byte>());

    Task<FindInPageEventArgs> IFindInPageAdapter.FindAsync(string text, FindInPageOptions? options)
        => Task.FromResult(new FindInPageEventArgs());

    void IFindInPageAdapter.StopFind(bool clearHighlights) { }

    double IZoomAdapter.ZoomFactor
    {
        get => 1.0;
        set { }
    }

    event EventHandler<double>? IZoomAdapter.ZoomFactorChanged
    {
        add { }
        remove { }
    }

    string IPreloadScriptAdapter.AddPreloadScript(string javaScript)
        => Guid.NewGuid().ToString("N");

    void IPreloadScriptAdapter.RemovePreloadScript(string scriptId) { }

    event EventHandler<ContextMenuRequestedEventArgs>? IContextMenuAdapter.ContextMenuRequested
    {
        add { }
        remove { }
    }

    void IDevToolsAdapter.OpenDevTools() { }

    void IDevToolsAdapter.CloseDevTools() { }

    bool IDevToolsAdapter.IsDevToolsOpen => false;
}
