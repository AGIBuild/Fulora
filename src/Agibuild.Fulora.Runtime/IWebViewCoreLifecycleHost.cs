namespace Agibuild.Fulora;

/// <summary>
/// Common lifecycle-state facade shared by runtime host contracts.
/// </summary>
/// <remarks>
/// Implemented explicitly by <see cref="WebViewCore"/>. Runtime collaborators observe this base
/// contract (rather than the concrete core) so disposal and adapter-destruction observation stays a
/// single typed seam across every specialised <c>IWebViewCore*Host</c> interface.
/// </remarks>
internal interface IWebViewCoreLifecycleHost
{
    /// <summary>Gets a value indicating whether <see cref="WebViewCore"/> has been disposed.</summary>
    bool IsDisposed { get; }

    /// <summary>Gets a value indicating whether the underlying adapter has been destroyed.</summary>
    bool IsAdapterDestroyed { get; }
}
