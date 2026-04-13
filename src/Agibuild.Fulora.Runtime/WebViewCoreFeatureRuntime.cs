using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal interface IWebViewCoreFeatureHost
{
    bool IsDisposed { get; }

    bool IsAdapterDestroyed { get; }

    Task EnqueueOperationAsync(string operationType, Func<Task> func);

    Task<T> EnqueueOperationAsync<T>(string operationType, Func<Task<T>> func);

    void ObserveBackgroundTask(Task task, string operationType);

    void ThrowIfDisposed();

    void RaiseZoomFactorChanged(double zoomFactor);

    void RaiseContextMenuRequested(ContextMenuRequestedEventArgs args);

    void RaiseDragEntered(DragEventArgs args);

    void RaiseDragOver(DragEventArgs args);

    void RaiseDragLeft();

    void RaiseDropCompleted(DropEventArgs args);
}

internal sealed class WebViewCoreFeatureRuntime : IDisposable, IWebViewCoreFeatureOperations
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 5.0;

    private readonly IWebViewCoreFeatureHost _host;
    private readonly IWebViewAdapter _adapter;
    private readonly IWebViewDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly IScreenshotAdapter? _screenshotAdapter;
    private readonly IPrintAdapter? _printAdapter;
    private readonly IFindInPageAdapter? _findInPageAdapter;
    private readonly IZoomAdapter? _zoomAdapter;
    private readonly IPreloadScriptAdapter? _preloadScriptAdapter;
    private readonly IAsyncPreloadScriptAdapter? _asyncPreloadScriptAdapter;
    private readonly IContextMenuAdapter? _contextMenuAdapter;
    private readonly IDragDropAdapter? _dragDropAdapter;

    public WebViewCoreFeatureRuntime(
        IWebViewCoreFeatureHost host,
        IWebViewAdapter adapter,
        IWebViewDispatcher dispatcher,
        ILogger logger,
        IWebViewEnvironmentOptions environmentOptions)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(environmentOptions);

        _screenshotAdapter = _adapter as IScreenshotAdapter;
        _logger.LogDebug("Screenshot support: {Supported}", _screenshotAdapter is not null);

        _printAdapter = _adapter as IPrintAdapter;
        _logger.LogDebug("Print support: {Supported}", _printAdapter is not null);

        _findInPageAdapter = _adapter as IFindInPageAdapter;
        _logger.LogDebug("Find-in-page support: {Supported}", _findInPageAdapter is not null);

        _zoomAdapter = _adapter as IZoomAdapter;
        if (_zoomAdapter is not null)
        {
            _zoomAdapter.ZoomFactorChanged += OnAdapterZoomFactorChanged;
        }

        _logger.LogDebug("Zoom support: {Supported}", _zoomAdapter is not null);

        _preloadScriptAdapter = _adapter as IPreloadScriptAdapter;
        _asyncPreloadScriptAdapter = _adapter as IAsyncPreloadScriptAdapter;
        if (_preloadScriptAdapter is not null)
        {
            var globalScripts = environmentOptions.PreloadScripts;
            foreach (var script in globalScripts)
            {
                _preloadScriptAdapter.AddPreloadScript(script);
            }

            if (globalScripts.Count > 0)
            {
                _logger.LogDebug("Global preload scripts applied: {Count}", globalScripts.Count);
            }
        }

        _logger.LogDebug("Preload script support: {Supported}", _preloadScriptAdapter is not null);

        _contextMenuAdapter = _adapter as IContextMenuAdapter;
        if (_contextMenuAdapter is not null)
        {
            _contextMenuAdapter.ContextMenuRequested += OnAdapterContextMenuRequested;
        }

        _logger.LogDebug("Context menu support: {Supported}", _contextMenuAdapter is not null);

        _dragDropAdapter = _adapter as IDragDropAdapter;
        if (_dragDropAdapter is not null)
        {
            _dragDropAdapter.DragEntered += OnAdapterDragEntered;
            _dragDropAdapter.DragOver += OnAdapterDragOver;
            _dragDropAdapter.DragLeft += OnAdapterDragLeft;
            _dragDropAdapter.DropCompleted += OnAdapterDropCompleted;
        }

        _logger.LogDebug("Drag-drop support: {Supported}", _dragDropAdapter is not null);
    }

    public bool HasDragDropSupport => _dragDropAdapter is not null;

    public Task OpenDevToolsAsync()
    {
        return _host.EnqueueOperationAsync(nameof(OpenDevToolsAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_adapter is IDevToolsAdapter devTools)
            {
                devTools.OpenDevTools();
            }
            else
            {
                _logger.LogDebug("DevTools: adapter does not support runtime toggle");
            }

            return Task.CompletedTask;
        });
    }

    public Task CloseDevToolsAsync()
    {
        return _host.EnqueueOperationAsync(nameof(CloseDevToolsAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_adapter is IDevToolsAdapter devTools)
            {
                devTools.CloseDevTools();
            }

            return Task.CompletedTask;
        });
    }

    public Task<bool> IsDevToolsOpenAsync()
    {
        return _host.EnqueueOperationAsync(nameof(IsDevToolsOpenAsync), () =>
        {
            _host.ThrowIfDisposed();
            return Task.FromResult(_adapter is IDevToolsAdapter devTools && devTools.IsDevToolsOpen);
        });
    }

    public Task<byte[]> CaptureScreenshotAsync()
    {
        return _host.EnqueueOperationAsync(nameof(CaptureScreenshotAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_screenshotAdapter is null)
            {
                throw new NotSupportedException("The current WebView adapter does not support screenshot capture.");
            }

            return _screenshotAdapter.CaptureScreenshotAsync();
        });
    }

    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null)
    {
        return _host.EnqueueOperationAsync(nameof(PrintToPdfAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_printAdapter is null)
            {
                throw new NotSupportedException("The current WebView adapter does not support PDF printing.");
            }

            return _printAdapter.PrintToPdfAsync(options);
        });
    }

    public Task<double> GetZoomFactorAsync()
    {
        return _host.EnqueueOperationAsync(nameof(GetZoomFactorAsync), () =>
        {
            _host.ThrowIfDisposed();
            return Task.FromResult(_zoomAdapter?.ZoomFactor ?? 1.0);
        });
    }

    public Task SetZoomFactorAsync(double zoomFactor)
    {
        return _host.EnqueueOperationAsync(nameof(SetZoomFactorAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_zoomAdapter is null)
            {
                return Task.CompletedTask;
            }

            _zoomAdapter.ZoomFactor = Math.Clamp(zoomFactor, MinZoom, MaxZoom);
            return Task.CompletedTask;
        });
    }

    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
    {
        return _host.EnqueueOperationAsync(nameof(FindInPageAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("Search text must not be null or empty.", nameof(text));
            }

            if (_findInPageAdapter is null)
            {
                throw new NotSupportedException("The current WebView adapter does not support find-in-page.");
            }

            return _findInPageAdapter.FindAsync(text, options);
        });
    }

    public Task StopFindInPageAsync(bool clearHighlights = true)
    {
        return _host.EnqueueOperationAsync(nameof(StopFindInPageAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_findInPageAdapter is null)
            {
                throw new NotSupportedException("The current WebView adapter does not support find-in-page.");
            }

            _findInPageAdapter.StopFind(clearHighlights);
            return Task.CompletedTask;
        });
    }

    public Task<string> AddPreloadScriptAsync(string javaScript)
    {
        return _host.EnqueueOperationAsync(nameof(AddPreloadScriptAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_asyncPreloadScriptAdapter is not null)
            {
                return _asyncPreloadScriptAdapter.AddPreloadScriptAsync(javaScript);
            }

            if (_preloadScriptAdapter is null)
            {
                throw new NotSupportedException("The current WebView adapter does not support preload scripts.");
            }

            return Task.FromResult(_preloadScriptAdapter.AddPreloadScript(javaScript));
        });
    }

    public Task RemovePreloadScriptAsync(string scriptId)
    {
        return _host.EnqueueOperationAsync(nameof(RemovePreloadScriptAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_asyncPreloadScriptAdapter is not null)
            {
                return _asyncPreloadScriptAdapter.RemovePreloadScriptAsync(scriptId);
            }

            if (_preloadScriptAdapter is null)
            {
                throw new NotSupportedException("The current WebView adapter does not support preload scripts.");
            }

            _preloadScriptAdapter.RemovePreloadScript(scriptId);
            return Task.CompletedTask;
        });
    }

    public Task<INativeHandle?> TryGetWebViewHandleAsync()
    {
        if (_host.IsAdapterDestroyed)
        {
            return Task.FromResult<INativeHandle?>(null);
        }

        if (_adapter is not INativeWebViewHandleProvider provider)
        {
            return Task.FromResult<INativeHandle?>(null);
        }

        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(provider.TryGetWebViewHandle());
        }

        return _dispatcher.InvokeAsync(() => provider.TryGetWebViewHandle());
    }

    public void SetCustomUserAgent(string? userAgent)
    {
        _host.ThrowIfDisposed();
        if (_adapter is not IWebViewAdapterOptions adapterOptions)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            adapterOptions.SetCustomUserAgent(userAgent);
        }
        else
        {
            var dispatchTask = _dispatcher.InvokeAsync(() => adapterOptions.SetCustomUserAgent(userAgent));
            _host.ObserveBackgroundTask(dispatchTask, nameof(SetCustomUserAgent));
        }

        _logger.LogDebug("CustomUserAgent set to: {UA}", userAgent ?? "(default)");
    }

    public void Dispose()
    {
        if (_zoomAdapter is not null)
        {
            _zoomAdapter.ZoomFactorChanged -= OnAdapterZoomFactorChanged;
        }

        if (_contextMenuAdapter is not null)
        {
            _contextMenuAdapter.ContextMenuRequested -= OnAdapterContextMenuRequested;
        }

        if (_dragDropAdapter is not null)
        {
            _dragDropAdapter.DragEntered -= OnAdapterDragEntered;
            _dragDropAdapter.DragOver -= OnAdapterDragOver;
            _dragDropAdapter.DragLeft -= OnAdapterDragLeft;
            _dragDropAdapter.DropCompleted -= OnAdapterDropCompleted;
        }
    }

    private void OnAdapterZoomFactorChanged(object? sender, double newZoom)
    {
        if (_host.IsDisposed)
        {
            return;
        }

        _ = _dispatcher.InvokeAsync(() =>
        {
            _host.RaiseZoomFactorChanged(newZoom);
            return Task.CompletedTask;
        });
    }

    private void OnAdapterContextMenuRequested(object? sender, ContextMenuRequestedEventArgs args)
    {
        if (_host.IsDisposed)
        {
            return;
        }

        _ = _dispatcher.InvokeAsync(() =>
        {
            _host.RaiseContextMenuRequested(args);
            return Task.CompletedTask;
        });
    }

    private void OnAdapterDragEntered(object? sender, DragEventArgs args)
        => _host.RaiseDragEntered(args);

    private void OnAdapterDragOver(object? sender, DragEventArgs args)
        => _host.RaiseDragOver(args);

    private void OnAdapterDragLeft(object? sender, EventArgs args)
        => _host.RaiseDragLeft();

    private void OnAdapterDropCompleted(object? sender, DropEventArgs args)
        => _host.RaiseDropCompleted(args);
}
