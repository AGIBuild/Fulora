using Agibuild.Fulora;

namespace Agibuild.Fulora.Adapters.Abstractions;

internal interface ICookieAdapter
{
    Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri);
    Task SetCookieAsync(WebViewCookie cookie);
    Task DeleteCookieAsync(WebViewCookie cookie);
    Task ClearAllCookiesAsync();
}

/// <summary>
/// Optional interface for adapters that support environment options (DevTools, UserAgent, Ephemeral).
/// Runtime checks for this via <c>adapter as IWebViewAdapterOptions</c>.
/// Must be called before <see cref="IWebViewAdapter.Attach"/>.
/// </summary>
internal interface IWebViewAdapterOptions
{
    void ApplyEnvironmentOptions(IWebViewEnvironmentOptions options);
    void SetCustomUserAgent(string? userAgent);
}

/// <summary>
/// Optional interface for adapters that support runtime DevTools toggling.
/// </summary>
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
/// Optional interface for adapters that support custom URI scheme registration.
/// Runtime checks for this via <c>adapter as ICustomSchemeAdapter</c>.
/// <see cref="RegisterCustomSchemes"/> is called before <see cref="IWebViewAdapter.Attach"/>.
/// </summary>
internal interface ICustomSchemeAdapter
{
    void RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes);
}

/// <summary>
/// Optional interface for adapters that support download interception.
/// Runtime checks for this via <c>adapter as IDownloadAdapter</c>.
/// </summary>
internal interface IDownloadAdapter
{
    event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
}

/// <summary>
/// Optional interface for adapters that support permission request interception.
/// Runtime checks for this via <c>adapter as IPermissionAdapter</c>.
/// </summary>
internal interface IPermissionAdapter
{
    event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
}

/// <summary>
/// Optional interface for adapters that support editing commands (copy, paste, etc.).
/// Runtime checks for this via <c>adapter as ICommandAdapter</c>.
/// </summary>
internal interface ICommandAdapter
{
    /// <summary>Executes a standard editing command on the WebView.</summary>
    void ExecuteCommand(WebViewCommand command);
}

/// <summary>
/// Optional interface for adapters that support context menu interception.
/// Runtime checks for this via <c>adapter as IContextMenuAdapter</c>.
/// </summary>
internal interface IContextMenuAdapter
{
    /// <summary>Raised when the user triggers a context menu (right-click, long-press).</summary>
    event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
}

/// <summary>
/// Optional interface for adapters that support preload scripts (user scripts injected at document start).
/// Runtime checks for this via <c>adapter as IPreloadScriptAdapter</c>.
/// </summary>
internal interface IPreloadScriptAdapter
{
    /// <summary>Registers a JavaScript snippet to run at document start on every page load. Returns an opaque script ID.</summary>
    string AddPreloadScript(string javaScript);
    /// <summary>Removes a previously registered preload script by its ID.</summary>
    void RemovePreloadScript(string scriptId);
}

/// <summary>
/// Optional async preload script interface for adapters that can register scripts without blocking the UI thread.
/// Runtime prefers this over <see cref="IPreloadScriptAdapter"/> when available.
/// </summary>
internal interface IAsyncPreloadScriptAdapter
{
    /// <summary>Registers a JavaScript snippet to run at document start on every page load. Returns an opaque script ID.</summary>
    Task<string> AddPreloadScriptAsync(string javaScript);
    /// <summary>Removes a previously registered preload script by its ID.</summary>
    Task RemovePreloadScriptAsync(string scriptId);
}

/// <summary>
/// Optional interface for adapters that support zoom level control.
/// Runtime checks for this via <c>adapter as IZoomAdapter</c>.
/// </summary>
internal interface IZoomAdapter
{
    /// <summary>Gets or sets the zoom factor (1.0 = 100%).</summary>
    double ZoomFactor { get; set; }
    /// <summary>Raised when the zoom factor changes (programmatic or user-initiated).</summary>
    event EventHandler<double>? ZoomFactorChanged;
}

/// <summary>
/// Optional interface for adapters that support in-page text search.
/// Runtime checks for this via <c>adapter as IFindInPageAdapter</c>.
/// </summary>
internal interface IFindInPageAdapter
{
    /// <summary>Searches the current page for the given text. Returns match info.</summary>
    Task<FindInPageEventArgs> FindAsync(string text, FindInPageOptions? options);
    /// <summary>Clears find highlights and resets search state.</summary>
    void StopFind(bool clearHighlights = true);
}

/// <summary>
/// Optional interface for adapters that support screenshot capture.
/// Runtime checks for this via <c>adapter as IScreenshotAdapter</c>.
/// </summary>
internal interface IScreenshotAdapter
{
    /// <summary>Captures the current viewport as a PNG byte array.</summary>
    Task<byte[]> CaptureScreenshotAsync();
}

/// <summary>
/// Optional interface for adapters that support PDF printing.
/// Runtime checks for this via <c>adapter as IPrintAdapter</c>.
/// </summary>
internal interface IPrintAdapter
{
    /// <summary>Prints the current page to a PDF byte array.</summary>
    Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options);
}

internal interface IWebViewAdapter
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
/// Optional interface for adapters that support native ↔ web drag-and-drop.
/// Runtime checks for this via <c>adapter as IDragDropAdapter</c>.
/// </summary>
internal interface IDragDropAdapter
{
    event EventHandler<DragEventArgs>? DragEntered;
    event EventHandler<DragEventArgs>? DragOver;
    event EventHandler<EventArgs>? DragLeft;
    event EventHandler<DropEventArgs>? DropCompleted;
}
