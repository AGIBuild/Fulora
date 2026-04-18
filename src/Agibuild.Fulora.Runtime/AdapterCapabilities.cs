using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora;

/// <summary>
/// Immutable snapshot of the <em>truly optional</em> capability interfaces an
/// <see cref="IWebViewAdapter"/> implements. All mandatory capabilities
/// (cookies, commands, zoom, find-in-page, preload, screenshot, print,
/// context-menu, dev-tools, custom-scheme, download, permission,
/// adapter-options) are part of <see cref="IWebViewAdapter"/> itself and
/// require no negotiation — the runtime invokes them directly on the adapter
/// reference.
/// </summary>
/// <remarks>
/// Only two capabilities stay opt-in and therefore need a slot here:
/// <list type="bullet">
///   <item><description><see cref="IDragDropAdapter"/> — Android WebView has no
///   native drag-and-drop APIs.</description></item>
///   <item><description><see cref="IAsyncPreloadScriptAdapter"/> — an async
///   refinement of <see cref="IPreloadScriptAdapter"/>; only Windows WebView2
///   currently exposes a native async preload entry point.</description></item>
/// </list>
/// The probe runs exactly once per adapter instance during
/// <c>WebViewCore</c> construction. A <see langword="null"/> slot means "not
/// supported by this adapter".
/// </remarks>
internal readonly record struct AdapterCapabilities(
    IDragDropAdapter? DragDrop,
    IAsyncPreloadScriptAdapter? AsyncPreloadScript)
{
    /// <summary>
    /// Performs the one-shot optional-capability negotiation against
    /// <paramref name="adapter"/>, producing a snapshot of the two opt-in
    /// slots.
    /// </summary>
    public static AdapterCapabilities From(IWebViewAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return new AdapterCapabilities(
            DragDrop: adapter as IDragDropAdapter,
            AsyncPreloadScript: adapter as IAsyncPreloadScriptAdapter);
    }
}
