using Avalonia;

namespace Agibuild.Fulora;

/// <summary>
/// Manages a companion overlay for rendering Avalonia controls above WebView.
/// Core behavior: tracks WebView bounds and renders OverlayContent.
/// </summary>
public sealed class WebViewOverlayHost : IDisposable
{
    private readonly WebView _owner;
    private bool _isVisible;
    private bool _disposed;

    internal WebViewOverlayHost(WebView owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// The Avalonia visual to render in the overlay.
    /// </summary>
    public object? Content { get; set; }

    /// <summary>
    /// Whether the overlay is currently visible.
    /// </summary>
    public bool IsVisible => _isVisible;

    /// <summary>
    /// Updates the overlay position to match WebView bounds.
    /// </summary>
    internal void UpdatePosition(Rect webViewBounds, Point screenOffset, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Position sync - placeholder for platform-specific overlay positioning.
        // Actual implementation will be provided by platform adapters.
    }

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isVisible = true;
    }

    internal void Hide()
    {
        _isVisible = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Content = null;
        _isVisible = false;
    }
}
