namespace Agibuild.Fulora;

/// <summary>
/// Capability: retrieve the underlying platform-native WebView handle. The handle
/// is only valid between <c>AdapterCreated</c> and <c>AdapterDestroyed</c>.
/// </summary>
public interface IWebViewNativeHandle
{
    /// <summary>
    /// Asynchronously retrieves the native platform WebView handle, or
    /// <see langword="null"/> when the adapter has been destroyed.
    /// </summary>
    Task<INativeHandle?> TryGetWebViewHandleAsync();
}
