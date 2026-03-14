using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Agibuild.Fulora;

#pragma warning disable CS1591

public interface IWebView : IDisposable
{
    Uri Source { get; set; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    bool IsLoading { get; }
    Guid ChannelId { get; }

    Task NavigateAsync(Uri uri);
    Task NavigateToStringAsync(string html);
    Task NavigateToStringAsync(string html, Uri? baseUrl);
    Task<string?> InvokeScriptAsync(string script);

    Task<bool> GoBackAsync();
    Task<bool> GoForwardAsync();
    Task<bool> RefreshAsync();
    Task<bool> StopAsync();

    ICookieManager? TryGetCookieManager();
    ICommandManager? TryGetCommandManager();
    Task<INativeHandle?> TryGetWebViewHandleAsync();

    IWebViewRpcService? Rpc { get; }
    IBridgeTracer? BridgeTracer { get; set; }
    IBridgeService Bridge { get; }

    Task OpenDevToolsAsync();
    Task CloseDevToolsAsync();
    Task<bool> IsDevToolsOpenAsync();

    Task<byte[]> CaptureScreenshotAsync();
    Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null);

    event EventHandler<NavigationStartingEventArgs>? NavigationStarted;
    event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
    event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;
    event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
    event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
    event EventHandler<AdapterCreatedEventArgs>? AdapterCreated;
    event EventHandler? AdapterDestroyed;

    Task<double> GetZoomFactorAsync();
    Task SetZoomFactorAsync(double zoomFactor);

    Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null);
    Task StopFindInPageAsync(bool clearHighlights = true);

    Task<string> AddPreloadScriptAsync(string javaScript);
    Task RemovePreloadScriptAsync(string scriptId);

    event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
}

/// <summary>
/// Marks an <see cref="IWebView"/> implementation that can enable framework SPA hosting.
/// </summary>
public interface ISpaHostingWebView : IWebView
{
    void EnableSpaHosting(SpaHostingOptions options);
}

public interface IWebDialog : IWebView
{
    string? Title { get; set; }
    bool CanUserResize { get; set; }

    void Show();
    bool Show(INativeHandle owner);
    void Close();
    bool Resize(int width, int height);
    bool Move(int x, int y);

    event EventHandler? Closing;
}

public interface IWebAuthBroker
{
    Task<WebAuthResult> AuthenticateAsync(ITopLevelWindow owner, AuthOptions options);
}

public interface IWebViewDispatcher
{
    bool CheckAccess();
    Task InvokeAsync(Action action);
    Task<T> InvokeAsync<T>(Func<T> func);
    Task InvokeAsync(Func<Task> func);
    Task<T> InvokeAsync<T>(Func<Task<T>> func);
}

internal interface IWebViewAdapterHost
{
    Guid ChannelId { get; }
    ValueTask<NativeNavigationStartingDecision> OnNativeNavigationStartingAsync(NativeNavigationStartingInfo info);
}

public interface IWebMessagePolicy
{
    WebMessagePolicyDecision Evaluate(in WebMessageEnvelope envelope);
}

public interface IWebMessageDropDiagnosticsSink
{
    void OnMessageDropped(in WebMessageDropDiagnostic diagnostic);
}

public interface IWebViewEnvironmentOptions
{
    bool EnableDevTools { get; set; }
    string? CustomUserAgent { get; set; }
    bool UseEphemeralSession { get; set; }
    bool TransparentBackground { get => false; set { } }
    IReadOnlyList<CustomSchemeRegistration> CustomSchemes { get; }
    IReadOnlyList<string> PreloadScripts { get; }
}

public interface INativeHandle
{
    nint Handle { get; }
    string HandleDescriptor { get; }
}

public interface INativeWebViewHandleProvider
{
    INativeHandle? TryGetWebViewHandle();
}

public interface IWindowsWebView2PlatformHandle : INativeHandle
{
    nint CoreWebView2Handle { get; }
    nint CoreWebView2ControllerHandle { get; }
}

public interface IAppleWKWebViewPlatformHandle : INativeHandle
{
    nint WKWebViewHandle { get; }
}

public interface IGtkWebViewPlatformHandle : INativeHandle
{
    nint WebKitWebViewHandle { get; }
}

public interface IAndroidWebViewPlatformHandle : INativeHandle
{
    nint AndroidWebViewHandle { get; }
}

[Experimental("AGWV001")]
public interface ICookieManager
{
    Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri);
    Task SetCookieAsync(WebViewCookie cookie);
    Task DeleteCookieAsync(WebViewCookie cookie);
    Task ClearAllCookiesAsync();
}

public interface ICommandManager
{
    Task CopyAsync();
    Task CutAsync();
    Task PasteAsync();
    Task SelectAllAsync();
    Task UndoAsync();
    Task RedoAsync();
}

public interface ITopLevelWindow
{
    INativeHandle? PlatformHandle { get; }
}

public interface IWebDialogFactory
{
    IWebDialog Create(IWebViewEnvironmentOptions? options = null);
}

public interface IWebViewRpcService
{
    void Handle(string method, Func<JsonElement?, Task<object?>> handler);
    void Handle(string method, Func<JsonElement?, object?> handler);
    void Handle(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => Handle(method, args => handler(args, CancellationToken.None));

    void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose);
    void UnregisterHandler(string method);

    Task<JsonElement> InvokeAsync(string method, object? args = null);
    Task<T?> InvokeAsync<T>(string method, object? args = null);
    Task<JsonElement> InvokeAsync(string method, object? args, CancellationToken cancellationToken)
        => InvokeAsync(method, args);
    Task<T?> InvokeAsync<T>(string method, object? args, CancellationToken cancellationToken)
        => InvokeAsync<T>(method, args);

    Task NotifyAsync(string method, object? args = null)
    {
        return Task.CompletedTask;
    }
}

#pragma warning restore CS1591
