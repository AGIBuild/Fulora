using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora;

/// <summary>
/// Immutable snapshot of the optional capability interfaces an <see cref="IWebViewAdapter"/>
/// implements. Created once during <c>WebViewCore</c> construction via
/// <see cref="From(IWebViewAdapter)"/> and consumed by every runtime that needs to gate features
/// on platform support.
/// </summary>
/// <remarks>
/// Runtimes must prefer these cached slots over ad-hoc <c>adapter as IXxxAdapter</c> checks so
/// that:
/// <list type="bullet">
///   <item>capability probing happens exactly once per adapter instance,</item>
///   <item>new capabilities are added by extending this record (single source of truth), and</item>
///   <item>the adapter object itself is only passed around for execution-style APIs, not for
///   capability negotiation.</item>
/// </list>
/// The record itself is a <see langword="readonly"/> value type — zero allocation per pass, no
/// hidden side effects.
/// </remarks>
internal readonly record struct AdapterCapabilities(
    IWebViewAdapterOptions? Options,
    ICustomSchemeAdapter? CustomScheme,
    ICookieAdapter? Cookie,
    ICommandAdapter? Command,
    IScreenshotAdapter? Screenshot,
    IPrintAdapter? Print,
    IFindInPageAdapter? FindInPage,
    IZoomAdapter? Zoom,
    IPreloadScriptAdapter? PreloadScript,
    IAsyncPreloadScriptAdapter? AsyncPreloadScript,
    IContextMenuAdapter? ContextMenu,
    IDragDropAdapter? DragDrop,
    IDevToolsAdapter? DevTools,
    IDownloadAdapter? Download,
    IPermissionAdapter? Permission,
    INativeWebViewHandleProvider? NativeHandleProvider)
{
    /// <summary>
    /// Performs the one-shot capability negotiation against <paramref name="adapter"/>, producing
    /// a fully-populated snapshot. Null-safe on each slot; a null slot means "capability not
    /// supported by this adapter".
    /// </summary>
    public static AdapterCapabilities From(IWebViewAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return new AdapterCapabilities(
            Options: adapter as IWebViewAdapterOptions,
            CustomScheme: adapter as ICustomSchemeAdapter,
            Cookie: adapter as ICookieAdapter,
            Command: adapter as ICommandAdapter,
            Screenshot: adapter as IScreenshotAdapter,
            Print: adapter as IPrintAdapter,
            FindInPage: adapter as IFindInPageAdapter,
            Zoom: adapter as IZoomAdapter,
            PreloadScript: adapter as IPreloadScriptAdapter,
            AsyncPreloadScript: adapter as IAsyncPreloadScriptAdapter,
            ContextMenu: adapter as IContextMenuAdapter,
            DragDrop: adapter as IDragDropAdapter,
            DevTools: adapter as IDevToolsAdapter,
            Download: adapter as IDownloadAdapter,
            Permission: adapter as IPermissionAdapter,
            NativeHandleProvider: adapter as INativeWebViewHandleProvider);
    }
}
