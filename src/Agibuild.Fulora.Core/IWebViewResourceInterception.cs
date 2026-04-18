namespace Agibuild.Fulora;

/// <summary>
/// Capability: intercept and inspect outbound HTTP(S) requests before the adapter
/// issues them, and observe environment-initialization events.
/// </summary>
/// <remarks>
/// Experimental-API markers on the event argument types
/// (<c>WebResourceRequestedEventArgs</c>, <c>EnvironmentRequestedEventArgs</c>)
/// continue to apply to consumers; see <c>docs/API_SURFACE_REVIEW.md</c>. No
/// attribute is added at the event level because the original members on
/// <c>IWebViewFeatures</c> carry none.
/// </remarks>
public interface IWebViewResourceInterception
{
    /// <summary>
    /// Raised for every outbound resource request. Handlers may substitute the
    /// response via <c>WebResourceRequestedEventArgs</c>.
    /// </summary>
    event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;

    /// <summary>
    /// Raised once per adapter to allow late binding of the environment options
    /// before the native WebView process starts.
    /// </summary>
    event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;
}
