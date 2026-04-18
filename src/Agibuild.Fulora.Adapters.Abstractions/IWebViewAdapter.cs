using Agibuild.Fulora;

namespace Agibuild.Fulora.Adapters.Abstractions;

// ---------------------------------------------------------------------------
// Mandatory capability facets.
//
// Every facet below is a sub-interface of IWebViewAdapter. All adapters must
// implement every facet; splitting them exists only as a documentation and
// semantic grouping device ("what domain does this method belong to?"). The
// runtime invokes these members directly on IWebViewAdapter without any
// `adapter as IXxxAdapter` / null-propagation — capability negotiation has
// been removed for the mandatory set.
//
// Two truly-optional facets are kept as standalone interfaces that are NOT
// inherited by IWebViewAdapter:
//   * IDragDropAdapter        — Android WebView exposes no native DnD APIs.
//   * IAsyncPreloadScriptAdapter — An opt-in async refinement of
//                                  IPreloadScriptAdapter, only Windows offers it
//                                  today because WebView2 exposes an async
//                                  AddScriptToExecuteOnDocumentCreatedAsync.
// Those two remain negotiated through AdapterCapabilities.
// ---------------------------------------------------------------------------

internal interface ICookieAdapter
{
    Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri);
    Task SetCookieAsync(WebViewCookie cookie);
    Task DeleteCookieAsync(WebViewCookie cookie);
    Task ClearAllCookiesAsync();
}

/// <summary>
/// Environment option application (DevTools, UserAgent, Ephemeral). Must be
/// invoked before <see cref="IWebViewAdapter.Attach"/>.
/// </summary>
internal interface IWebViewAdapterOptions
{
    void ApplyEnvironmentOptions(IWebViewEnvironmentOptions options);
    void SetCustomUserAgent(string? userAgent);
}

/// <summary>Runtime DevTools toggling.</summary>
internal interface IDevToolsAdapter
{
    /// <summary>Opens the browser developer tools (inspector).</summary>
    void OpenDevTools();

    /// <summary>Closes the browser developer tools.</summary>
    void CloseDevTools();

    /// <summary>Returns whether developer tools are currently open.</summary>
    bool IsDevToolsOpen { get; }
}

/// <summary>
/// Custom URI scheme registration. Invoked before <see cref="IWebViewAdapter.Attach"/>.
/// </summary>
internal interface ICustomSchemeAdapter
{
    void RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes);
}

/// <summary>Download interception.</summary>
internal interface IDownloadAdapter
{
    event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
}

/// <summary>Permission-request interception.</summary>
internal interface IPermissionAdapter
{
    event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
}

/// <summary>Editing commands (copy, paste, etc.).</summary>
internal interface ICommandAdapter
{
    /// <summary>Executes a standard editing command on the WebView.</summary>
    void ExecuteCommand(WebViewCommand command);
}

/// <summary>Context-menu interception (right-click, long-press).</summary>
internal interface IContextMenuAdapter
{
    /// <summary>Raised when the user triggers a context menu (right-click, long-press).</summary>
    event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
}

/// <summary>Preload (document-start) script registration.</summary>
internal interface IPreloadScriptAdapter
{
    /// <summary>Registers a JavaScript snippet to run at document start on every page load. Returns an opaque script ID.</summary>
    string AddPreloadScript(string javaScript);
    /// <summary>Removes a previously registered preload script by its ID.</summary>
    void RemovePreloadScript(string scriptId);
}

/// <summary>
/// Truly-optional async preload-script refinement. The runtime prefers this
/// over <see cref="IPreloadScriptAdapter"/> when the adapter opts in. Absence
/// is surfaced through <c>AdapterCapabilities.AsyncPreloadScript</c>.
/// </summary>
internal interface IAsyncPreloadScriptAdapter
{
    /// <summary>Registers a JavaScript snippet to run at document start on every page load. Returns an opaque script ID.</summary>
    Task<string> AddPreloadScriptAsync(string javaScript);
    /// <summary>Removes a previously registered preload script by its ID.</summary>
    Task RemovePreloadScriptAsync(string scriptId);
}

/// <summary>Zoom-factor control.</summary>
internal interface IZoomAdapter
{
    /// <summary>Gets or sets the zoom factor (1.0 = 100%).</summary>
    double ZoomFactor { get; set; }
    /// <summary>Raised when the zoom factor changes (programmatic or user-initiated).</summary>
    event EventHandler<double>? ZoomFactorChanged;
}

/// <summary>In-page text search.</summary>
internal interface IFindInPageAdapter
{
    /// <summary>Searches the current page for the given text. Returns match info.</summary>
    Task<FindInPageEventArgs> FindAsync(string text, FindInPageOptions? options);
    /// <summary>Clears find highlights and resets search state.</summary>
    void StopFind(bool clearHighlights = true);
}

/// <summary>Screenshot capture.</summary>
internal interface IScreenshotAdapter
{
    /// <summary>Captures the current viewport as a PNG byte array.</summary>
    Task<byte[]> CaptureScreenshotAsync();
}

/// <summary>PDF printing.</summary>
internal interface IPrintAdapter
{
    /// <summary>Prints the current page to a PDF byte array.</summary>
    Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options);
}

/// <summary>
/// Primary adapter contract. <see cref="IWebViewAdapter"/> is the single source
/// of truth for every mandatory capability — implementers must satisfy every
/// inherited facet (cookies, commands, preload, zoom, …). Two facets remain
/// opt-in and are negotiated via <c>AdapterCapabilities</c>:
/// <see cref="IDragDropAdapter"/> and <see cref="IAsyncPreloadScriptAdapter"/>.
/// </summary>
internal interface IWebViewAdapter :
    INativeWebViewHandleProvider,
    IWebViewAdapterOptions,
    ICookieAdapter,
    ICommandAdapter,
    ICustomSchemeAdapter,
    IDownloadAdapter,
    IPermissionAdapter,
    IScreenshotAdapter,
    IPrintAdapter,
    IFindInPageAdapter,
    IZoomAdapter,
    IPreloadScriptAdapter,
    IContextMenuAdapter,
    IDevToolsAdapter
{
    void Initialize(IWebViewAdapterHost host);
    void Attach(INativeHandle parentHandle);
    void Detach();

    Task NavigateAsync(Guid navigationId, Uri uri);
    Task NavigateToStringAsync(Guid navigationId, string html);
    Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl);
    Task<string?> InvokeScriptAsync(string script);

    bool GoBack(Guid navigationId);
    bool GoForward(Guid navigationId);
    bool Refresh(Guid navigationId);
    bool Stop();

    bool CanGoBack { get; }
    bool CanGoForward { get; }

    event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
    event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;
}

/// <summary>
/// Truly-optional native ↔ web drag-and-drop. Android WebView exposes no
/// native DnD APIs, so this remains opt-in and is negotiated via
/// <c>AdapterCapabilities.DragDrop</c>.
/// </summary>
internal interface IDragDropAdapter
{
    event EventHandler<DragEventArgs>? DragEntered;
    event EventHandler<DragEventArgs>? DragOver;
    event EventHandler<EventArgs>? DragLeft;
    event EventHandler<DropEventArgs>? DropCompleted;
}
