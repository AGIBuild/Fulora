using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agibuild.Fulora;

internal sealed class WebViewControlLifecycleRuntime
{
    private readonly WebViewControlRuntime _controlRuntime;
    private readonly WebViewControlEventRuntime _eventRuntime;
    private readonly Func<ILoggerFactory?> _getLoggerFactory;
    private readonly Func<IWebViewEnvironmentOptions?> _getEnvironmentOptions;
    private readonly Func<Uri?> _getPendingSource;
    private readonly Action<bool> _setCoreAttached;
    private readonly Func<IWebViewDispatcher> _createDispatcher;
    private readonly Func<IWebViewDispatcher, ILogger<WebViewCore>, IWebViewEnvironmentOptions?, WebViewCore> _createCore;
    private readonly Func<IPlatformHandle, INativeHandle> _wrapPlatformHandle;

    public WebViewControlLifecycleRuntime(
        WebViewControlRuntime controlRuntime,
        WebViewControlEventRuntime eventRuntime,
        Func<ILoggerFactory?> getLoggerFactory,
        Func<IWebViewEnvironmentOptions?> getEnvironmentOptions,
        Func<Uri?> getPendingSource,
        Action<bool> setCoreAttached,
        Func<IWebViewDispatcher> createDispatcher,
        Func<IWebViewDispatcher, ILogger<WebViewCore>, IWebViewEnvironmentOptions?, WebViewCore>? createCore = null,
        Func<IPlatformHandle, INativeHandle>? wrapPlatformHandle = null)
    {
        _controlRuntime = controlRuntime ?? throw new ArgumentNullException(nameof(controlRuntime));
        _eventRuntime = eventRuntime ?? throw new ArgumentNullException(nameof(eventRuntime));
        _getLoggerFactory = getLoggerFactory ?? throw new ArgumentNullException(nameof(getLoggerFactory));
        _getEnvironmentOptions = getEnvironmentOptions ?? throw new ArgumentNullException(nameof(getEnvironmentOptions));
        _getPendingSource = getPendingSource ?? throw new ArgumentNullException(nameof(getPendingSource));
        _setCoreAttached = setCoreAttached ?? throw new ArgumentNullException(nameof(setCoreAttached));
        _createDispatcher = createDispatcher ?? throw new ArgumentNullException(nameof(createDispatcher));
        _createCore = createCore ?? WebViewCore.CreateForControl;
        _wrapPlatformHandle = wrapPlatformHandle ?? (handle => new AvaloniaNativeHandle(handle));
    }

    public void AttachToNativeControl(IPlatformHandle parentHandle)
    {
        ArgumentNullException.ThrowIfNull(parentHandle);

        WebViewCore? core = null;
        try
        {
            var dispatcher = _createDispatcher();
            var effectiveLoggerFactory = _getLoggerFactory() ?? WebViewEnvironment.LoggerFactory;
            var logger = effectiveLoggerFactory?.CreateLogger<WebViewCore>()
                         ?? (ILogger<WebViewCore>)NullLogger<WebViewCore>.Instance;

            core = _createCore(dispatcher, logger, _getEnvironmentOptions());
            _controlRuntime.AttachCore(core);
            _eventRuntime.Attach(core);

            core.Attach(_wrapPlatformHandle(parentHandle));
            _setCoreAttached(true);

            var pendingSource = _getPendingSource();
            if (pendingSource is not null)
                _ = core.NavigateAsync(pendingSource);
        }
        catch (PlatformNotSupportedException)
        {
            _eventRuntime.Detach();
            core?.Dispose();
            _setCoreAttached(false);
            _controlRuntime.MarkAdapterUnavailable();
        }
        catch
        {
            _eventRuntime.Detach();
            core?.Dispose();
            _setCoreAttached(false);
            _controlRuntime.ClearCore();
            throw;
        }
    }

    public void DestroyAttachedCore()
    {
        var coreAttached = _controlRuntime.IsCoreAttached;

        _eventRuntime.Detach();

        var core = _controlRuntime.Core;

        if (coreAttached)
        {
            core?.Detach();
            _setCoreAttached(false);
        }

        core?.Dispose();
        _controlRuntime.ClearCore();
    }

    private sealed class AvaloniaNativeHandle(IPlatformHandle inner) : INativeHandle
    {
        public nint Handle => inner.Handle;
        public string HandleDescriptor => inner.HandleDescriptor ?? string.Empty;
    }
}
