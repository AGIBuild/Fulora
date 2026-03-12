using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Agibuild.Fulora;

/// <summary>Result of hit-testing a point against the overlay.</summary>
public enum OverlayHitTestResult { Overlay, Passthrough }

/// <summary>
/// Manages a transparent companion window for rendering Avalonia controls above a WebView.
/// Uses a real borderless Avalonia <see cref="Window"/> as a child of the parent window,
/// positioned to exactly cover the WebView bounds.
/// </summary>
public sealed class WebViewOverlayHost : IDisposable
{
    private readonly WebView _owner;
    private Window? _overlayWindow;
    private object? _content;
    private bool _disposed;
    private bool _closingForDispose;
    private Rect _bounds;
    private double _dpiScale;
    private bool _isVisible;

    /// <summary>Whether the overlay currently has keyboard focus.</summary>
    public bool HasKeyboardFocus { get; set; }

    internal WebViewOverlayHost(WebView owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// The Avalonia visual to render in the overlay.
    /// Setting this updates the overlay window's content in real time.
    /// </summary>
    public object? Content
    {
        get => _content;
        set
        {
            _content = value;
            if (_overlayWindow is not null)
                _overlayWindow.Content = value;
        }
    }

    /// <summary>Whether the overlay is currently visible.</summary>
    public bool IsVisible => _isVisible;

    /// <summary>DPI-adjusted bounds of the overlay (screen physical pixels).</summary>
    public Rect Bounds => _bounds;

    /// <summary>Current DPI scale factor applied to bounds.</summary>
    public double DpiScale => _dpiScale;

    /// <summary>
    /// Updates the overlay position to match WebView bounds.
    /// Also repositions the real overlay window on screen.
    /// </summary>
    internal void UpdatePosition(Rect webViewBounds, Point screenOffset, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dpiScale = dpiScale;

        _bounds = new Rect(
            screenOffset.X + webViewBounds.X * dpiScale,
            screenOffset.Y + webViewBounds.Y * dpiScale,
            webViewBounds.Width * dpiScale,
            webViewBounds.Height * dpiScale);

        PositionOverlayWindow(webViewBounds);
    }

    /// <summary>Syncs overlay visibility with the WebView visibility.</summary>
    public void SyncVisibilityWith(bool isVisible)
    {
        if (isVisible && _content is not null)
            Show();
        else
            Hide();
    }

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isVisible = true;
        EnsureOverlayWindow();
    }

    internal void Hide()
    {
        _isVisible = false;
        if (_overlayWindow?.IsVisible == true)
            _overlayWindow.Hide();
    }

    /// <summary>
    /// Hit-tests a point to determine whether it should be handled by the overlay or passed through.
    /// </summary>
    public OverlayHitTestResult HitTest(double x, double y)
    {
        if (_content is null || !_isVisible) return OverlayHitTestResult.Passthrough;
        if (x >= _bounds.X && x <= _bounds.X + _bounds.Width &&
            y >= _bounds.Y && y <= _bounds.Y + _bounds.Height)
            return OverlayHitTestResult.Overlay;
        return OverlayHitTestResult.Passthrough;
    }

    /// <summary>Transfers keyboard focus from overlay to WebView.</summary>
    public void TransferFocusToWebView() => HasKeyboardFocus = false;

    /// <summary>Transfers keyboard focus from WebView to overlay.</summary>
    public void TransferFocusToOverlay() => HasKeyboardFocus = true;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isVisible = false;
        _content = null;
        DestroyOverlayWindow();
    }

    private void EnsureOverlayWindow()
    {
        if (_overlayWindow is not null)
        {
            if (!_overlayWindow.IsVisible)
                _overlayWindow.Show();
            return;
        }

        var parentWindow = TopLevel.GetTopLevel(_owner) as Window;
        if (parentWindow is null) return;

        _overlayWindow = new Window
        {
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            ShowInTaskbar = false,
            CanResize = false,
            Topmost = true,
            Content = _content,
            SizeToContent = SizeToContent.Manual,
        };

        _overlayWindow.Closing += OnOverlayClosing;
        _overlayWindow.Closed += OnOverlayClosed;
        _overlayWindow.Show(parentWindow);
    }

    private void OnOverlayClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_closingForDispose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnOverlayClosed(object? sender, EventArgs e) => _overlayWindow = null;

    private void PositionOverlayWindow(Rect webViewBounds)
    {
        if (_overlayWindow is null) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(_owner);
            if (topLevel is not null)
            {
                var localOrigin = _owner.TranslatePoint(new Point(0, 0), topLevel);
                if (localOrigin.HasValue)
                {
                    var screenPt = topLevel.PointToScreen(localOrigin.Value);
                    _overlayWindow.Position = new PixelPoint(screenPt.X, screenPt.Y);
                }
            }
        }
        catch
        {
            // TranslatePoint/PointToScreen may fail if not attached to visual tree
        }

        _overlayWindow.Width = webViewBounds.Width;
        _overlayWindow.Height = webViewBounds.Height;
    }

    private void DestroyOverlayWindow()
    {
        if (_overlayWindow is null) return;
        _closingForDispose = true;
        _overlayWindow.Closing -= OnOverlayClosing;
        _overlayWindow.Closed -= OnOverlayClosed;
        try { _overlayWindow.Close(); }
        catch { /* window may already be closed */ }
        _overlayWindow = null;
    }
}
