namespace Agibuild.Fulora;

/// <summary>
/// Minimal facade exposed by <see cref="WebViewCore"/> to <see cref="RuntimeCookieManager"/> and
/// <see cref="RuntimeCommandManager"/>, so those managers can live in their own files without
/// reaching into WebViewCore's private state.
/// </summary>
/// <remarks>
/// Implemented explicitly by <see cref="WebViewCore"/> so the underlying <c>EnqueueOperationAsync</c>
/// and <c>ThrowIfDisposed</c> methods stay private on the public type and are only observable via
/// this interface.
/// </remarks>
internal interface IWebViewCoreOperationHost : IWebViewCoreDisposalHost
{
    Task<T> EnqueueOperationAsync<T>(string operationType, Func<Task<T>> func);

    Task<object?> EnqueueOperationAsync(string operationType, Func<Task> func);
}
