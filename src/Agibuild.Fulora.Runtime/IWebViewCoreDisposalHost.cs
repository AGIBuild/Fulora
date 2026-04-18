namespace Agibuild.Fulora;

/// <summary>
/// Shared disposal-guard facade used by runtime collaborators that need to fail fast once
/// <see cref="WebViewCore"/> has been disposed.
/// </summary>
/// <remarks>
/// Implemented explicitly by <see cref="WebViewCore"/>, so the guard stays observable only through
/// the typed runtime host contracts that choose to enforce it.
/// </remarks>
internal interface IWebViewCoreDisposalHost
{
    /// <summary>Throws <see cref="ObjectDisposedException"/> when <see cref="WebViewCore"/> has been disposed.</summary>
    void ThrowIfDisposed();
}
