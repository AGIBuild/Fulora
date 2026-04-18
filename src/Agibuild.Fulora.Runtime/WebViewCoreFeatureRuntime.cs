using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal interface IWebViewCoreFeatureHost :
    IWebViewCoreLifecycleHost,
    IWebViewCoreDisposalHost,
    IWebViewCoreBackgroundTaskObserver
{
    Task EnqueueOperationAsync(string operationType, Func<Task> func);

    Task<T> EnqueueOperationAsync<T>(string operationType, Func<Task<T>> func);

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
    private readonly AdapterCapabilities _capabilities;
    private readonly IWebViewDispatcher _dispatcher;
    private readonly ILogger _logger;

    public WebViewCoreFeatureRuntime(
        IWebViewCoreFeatureHost host,
        IWebViewAdapter adapter,
        AdapterCapabilities capabilities,
        IWebViewDispatcher dispatcher,
        ILogger logger,
        IWebViewEnvironmentOptions environmentOptions)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _capabilities = capabilities;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(environmentOptions);

        // Mandatory facets (cookie / command / zoom / find / preload / screenshot / print /
        // context-menu / dev-tools) are inherited by IWebViewAdapter itself, so no probing is
        // required. Only drag-drop and async-preload remain opt-in.

        _adapter.ZoomFactorChanged += OnAdapterZoomFactorChanged;
        _adapter.ContextMenuRequested += OnAdapterContextMenuRequested;

        var globalScripts = environmentOptions.PreloadScripts;
        foreach (var script in globalScripts)
        {
            _adapter.AddPreloadScript(script);
        }

        if (globalScripts.Count > 0)
        {
            _logger.LogDebug("Global preload scripts applied: {Count}", globalScripts.Count);
        }

        _logger.LogDebug(
            "Async preload support: {Supported}",
            _capabilities.AsyncPreloadScript is not null);

        if (_capabilities.DragDrop is { } dragDrop)
        {
            dragDrop.DragEntered += OnAdapterDragEntered;
            dragDrop.DragOver += OnAdapterDragOver;
            dragDrop.DragLeft += OnAdapterDragLeft;
            dragDrop.DropCompleted += OnAdapterDropCompleted;
        }

        _logger.LogDebug("Drag-drop support: {Supported}", _capabilities.DragDrop is not null);
    }

    public bool HasDragDropSupport => _capabilities.DragDrop is not null;

    public Task OpenDevToolsAsync()
    {
        return _host.EnqueueOperationAsync(nameof(OpenDevToolsAsync), () =>
        {
            _host.ThrowIfDisposed();
            _adapter.OpenDevTools();
            return Task.CompletedTask;
        });
    }

    public Task CloseDevToolsAsync()
    {
        return _host.EnqueueOperationAsync(nameof(CloseDevToolsAsync), () =>
        {
            _host.ThrowIfDisposed();
            _adapter.CloseDevTools();
            return Task.CompletedTask;
        });
    }

    public Task<bool> IsDevToolsOpenAsync()
    {
        return _host.EnqueueOperationAsync(nameof(IsDevToolsOpenAsync), () =>
        {
            _host.ThrowIfDisposed();
            return Task.FromResult(_adapter.IsDevToolsOpen);
        });
    }

    public Task<byte[]> CaptureScreenshotAsync()
    {
        return _host.EnqueueOperationAsync(nameof(CaptureScreenshotAsync), () =>
        {
            _host.ThrowIfDisposed();
            return _adapter.CaptureScreenshotAsync();
        });
    }

    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null)
    {
        return _host.EnqueueOperationAsync(nameof(PrintToPdfAsync), () =>
        {
            _host.ThrowIfDisposed();
            return _adapter.PrintToPdfAsync(options);
        });
    }

    public Task<double> GetZoomFactorAsync()
    {
        return _host.EnqueueOperationAsync(nameof(GetZoomFactorAsync), () =>
        {
            _host.ThrowIfDisposed();
            return Task.FromResult(_adapter.ZoomFactor);
        });
    }

    public Task SetZoomFactorAsync(double zoomFactor)
    {
        return _host.EnqueueOperationAsync(nameof(SetZoomFactorAsync), () =>
        {
            _host.ThrowIfDisposed();
            _adapter.ZoomFactor = Math.Clamp(zoomFactor, MinZoom, MaxZoom);
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

            return _adapter.FindAsync(text, options);
        });
    }

    public Task StopFindInPageAsync(bool clearHighlights = true)
    {
        return _host.EnqueueOperationAsync(nameof(StopFindInPageAsync), () =>
        {
            _host.ThrowIfDisposed();
            _adapter.StopFind(clearHighlights);
            return Task.CompletedTask;
        });
    }

    public Task<string> AddPreloadScriptAsync(string javaScript)
    {
        return _host.EnqueueOperationAsync(nameof(AddPreloadScriptAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_capabilities.AsyncPreloadScript is { } asyncPreload)
            {
                return asyncPreload.AddPreloadScriptAsync(javaScript);
            }

            return Task.FromResult(_adapter.AddPreloadScript(javaScript));
        });
    }

    public Task RemovePreloadScriptAsync(string scriptId)
    {
        return _host.EnqueueOperationAsync(nameof(RemovePreloadScriptAsync), () =>
        {
            _host.ThrowIfDisposed();
            if (_capabilities.AsyncPreloadScript is { } asyncPreload)
            {
                return asyncPreload.RemovePreloadScriptAsync(scriptId);
            }

            _adapter.RemovePreloadScript(scriptId);
            return Task.CompletedTask;
        });
    }

    public Task<INativeHandle?> TryGetWebViewHandleAsync()
    {
        if (_host.IsAdapterDestroyed)
        {
            return Task.FromResult<INativeHandle?>(null);
        }

        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(_adapter.TryGetWebViewHandle());
        }

        return _dispatcher.InvokeAsync(() => _adapter.TryGetWebViewHandle());
    }

    public void SetCustomUserAgent(string? userAgent)
    {
        _host.ThrowIfDisposed();

        if (_dispatcher.CheckAccess())
        {
            _adapter.SetCustomUserAgent(userAgent);
        }
        else
        {
            var dispatchTask = _dispatcher.InvokeAsync(() => _adapter.SetCustomUserAgent(userAgent));
            _host.ObserveBackgroundTask(dispatchTask, nameof(SetCustomUserAgent));
        }

        _logger.LogDebug("CustomUserAgent set to: {UA}", userAgent ?? "(default)");
    }

    public void Dispose()
    {
        _adapter.ZoomFactorChanged -= OnAdapterZoomFactorChanged;
        _adapter.ContextMenuRequested -= OnAdapterContextMenuRequested;

        if (_capabilities.DragDrop is { } dragDrop)
        {
            dragDrop.DragEntered -= OnAdapterDragEntered;
            dragDrop.DragOver -= OnAdapterDragOver;
            dragDrop.DragLeft -= OnAdapterDragLeft;
            dragDrop.DropCompleted -= OnAdapterDropCompleted;
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
