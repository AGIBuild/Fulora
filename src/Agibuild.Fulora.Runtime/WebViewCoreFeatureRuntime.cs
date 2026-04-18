using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal sealed class WebViewCoreFeatureRuntime : IDisposable
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 5.0;

    private readonly WebViewCoreContext _context;

    public WebViewCoreFeatureRuntime(WebViewCoreContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // Mandatory facets (cookie / command / zoom / find / preload / screenshot / print /
        // context-menu / dev-tools) are inherited by IWebViewAdapter itself, so no probing is
        // required. Only drag-drop and async-preload remain opt-in.

        _context.Adapter.ZoomFactorChanged += OnAdapterZoomFactorChanged;
        _context.Adapter.ContextMenuRequested += OnAdapterContextMenuRequested;

        var globalScripts = _context.EnvironmentOptions.PreloadScripts;
        foreach (var script in globalScripts)
        {
            _context.Adapter.AddPreloadScript(script);
        }

        if (globalScripts.Count > 0)
        {
            _context.Logger.LogDebug("Global preload scripts applied: {Count}", globalScripts.Count);
        }

        _context.Logger.LogDebug(
            "Async preload support: {Supported}",
            _context.Capabilities.AsyncPreloadScript is not null);

        if (_context.Capabilities.DragDrop is { } dragDrop)
        {
            dragDrop.DragEntered += OnAdapterDragEntered;
            dragDrop.DragOver += OnAdapterDragOver;
            dragDrop.DragLeft += OnAdapterDragLeft;
            dragDrop.DropCompleted += OnAdapterDropCompleted;
        }

        _context.Logger.LogDebug("Drag-drop support: {Supported}", _context.Capabilities.DragDrop is not null);
    }

    public bool HasDragDropSupport => _context.Capabilities.DragDrop is not null;

    public Task OpenDevToolsAsync()
    {
        return _context.Operations.EnqueueAsync(nameof(OpenDevToolsAsync), () =>
        {
            _context.ThrowIfDisposed();
            _context.Adapter.OpenDevTools();
            return Task.CompletedTask;
        });
    }

    public Task CloseDevToolsAsync()
    {
        return _context.Operations.EnqueueAsync(nameof(CloseDevToolsAsync), () =>
        {
            _context.ThrowIfDisposed();
            _context.Adapter.CloseDevTools();
            return Task.CompletedTask;
        });
    }

    public Task<bool> IsDevToolsOpenAsync()
    {
        return _context.Operations.EnqueueAsync(nameof(IsDevToolsOpenAsync), () =>
        {
            _context.ThrowIfDisposed();
            return Task.FromResult(_context.Adapter.IsDevToolsOpen);
        });
    }

    public Task<byte[]> CaptureScreenshotAsync()
    {
        return _context.Operations.EnqueueAsync(nameof(CaptureScreenshotAsync), () =>
        {
            _context.ThrowIfDisposed();
            return _context.Adapter.CaptureScreenshotAsync();
        });
    }

    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null)
    {
        return _context.Operations.EnqueueAsync(nameof(PrintToPdfAsync), () =>
        {
            _context.ThrowIfDisposed();
            return _context.Adapter.PrintToPdfAsync(options);
        });
    }

    public Task<double> GetZoomFactorAsync()
    {
        return _context.Operations.EnqueueAsync(nameof(GetZoomFactorAsync), () =>
        {
            _context.ThrowIfDisposed();
            return Task.FromResult(_context.Adapter.ZoomFactor);
        });
    }

    public Task SetZoomFactorAsync(double zoomFactor)
    {
        return _context.Operations.EnqueueAsync(nameof(SetZoomFactorAsync), () =>
        {
            _context.ThrowIfDisposed();
            _context.Adapter.ZoomFactor = Math.Clamp(zoomFactor, MinZoom, MaxZoom);
            return Task.CompletedTask;
        });
    }

    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
    {
        return _context.Operations.EnqueueAsync(nameof(FindInPageAsync), () =>
        {
            _context.ThrowIfDisposed();
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("Search text must not be null or empty.", nameof(text));
            }

            return _context.Adapter.FindAsync(text, options);
        });
    }

    public Task StopFindInPageAsync(bool clearHighlights = true)
    {
        return _context.Operations.EnqueueAsync(nameof(StopFindInPageAsync), () =>
        {
            _context.ThrowIfDisposed();
            _context.Adapter.StopFind(clearHighlights);
            return Task.CompletedTask;
        });
    }

    public Task<string> AddPreloadScriptAsync(string javaScript)
    {
        return _context.Operations.EnqueueAsync(nameof(AddPreloadScriptAsync), () =>
        {
            _context.ThrowIfDisposed();
            if (_context.Capabilities.AsyncPreloadScript is { } asyncPreload)
            {
                return asyncPreload.AddPreloadScriptAsync(javaScript);
            }

            return Task.FromResult(_context.Adapter.AddPreloadScript(javaScript));
        });
    }

    public Task RemovePreloadScriptAsync(string scriptId)
    {
        return _context.Operations.EnqueueAsync(nameof(RemovePreloadScriptAsync), () =>
        {
            _context.ThrowIfDisposed();
            if (_context.Capabilities.AsyncPreloadScript is { } asyncPreload)
            {
                return asyncPreload.RemovePreloadScriptAsync(scriptId);
            }

            _context.Adapter.RemovePreloadScript(scriptId);
            return Task.CompletedTask;
        });
    }

    public Task<INativeHandle?> TryGetWebViewHandleAsync()
    {
        if (_context.Lifecycle.IsAdapterDestroyed)
        {
            return Task.FromResult<INativeHandle?>(null);
        }

        if (_context.Dispatcher.CheckAccess())
        {
            return Task.FromResult(_context.Adapter.TryGetWebViewHandle());
        }

        return _context.Dispatcher.InvokeAsync(() => _context.Adapter.TryGetWebViewHandle());
    }

    public void SetCustomUserAgent(string? userAgent)
    {
        _context.ThrowIfDisposed();

        if (_context.Dispatcher.CheckAccess())
        {
            _context.Adapter.SetCustomUserAgent(userAgent);
        }
        else
        {
            var dispatchTask = _context.Dispatcher.InvokeAsync(() => _context.Adapter.SetCustomUserAgent(userAgent));
            _context.ObserveBackgroundTask(dispatchTask, nameof(SetCustomUserAgent));
        }

        _context.Logger.LogDebug("CustomUserAgent set to: {UA}", userAgent ?? "(default)");
    }

    public void Dispose()
    {
        _context.Adapter.ZoomFactorChanged -= OnAdapterZoomFactorChanged;
        _context.Adapter.ContextMenuRequested -= OnAdapterContextMenuRequested;

        if (_context.Capabilities.DragDrop is { } dragDrop)
        {
            dragDrop.DragEntered -= OnAdapterDragEntered;
            dragDrop.DragOver -= OnAdapterDragOver;
            dragDrop.DragLeft -= OnAdapterDragLeft;
            dragDrop.DropCompleted -= OnAdapterDropCompleted;
        }
    }

    private void OnAdapterZoomFactorChanged(object? sender, double newZoom)
    {
        if (_context.Lifecycle.IsDisposed)
        {
            return;
        }

        _ = _context.Dispatcher.InvokeAsync(() =>
        {
            _context.Events.RaiseZoomFactorChanged(newZoom);
            return Task.CompletedTask;
        });
    }

    private void OnAdapterContextMenuRequested(object? sender, ContextMenuRequestedEventArgs args)
    {
        if (_context.Lifecycle.IsDisposed)
        {
            return;
        }

        _ = _context.Dispatcher.InvokeAsync(() =>
        {
            _context.Events.RaiseContextMenuRequested(args);
            return Task.CompletedTask;
        });
    }

    private void OnAdapterDragEntered(object? sender, DragEventArgs args)
        => _context.Events.RaiseDragEntered(args);

    private void OnAdapterDragOver(object? sender, DragEventArgs args)
        => _context.Events.RaiseDragOver(args);

    private void OnAdapterDragLeft(object? sender, EventArgs args)
        => _context.Events.RaiseDragLeft();

    private void OnAdapterDropCompleted(object? sender, DropEventArgs args)
        => _context.Events.RaiseDropCompleted(args);
}
