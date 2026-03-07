using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Agibuild.Fulora.NativeOverlay;

/// <summary>
/// Linux native overlay using GTK3 popup window with RGBA visual for transparency.
/// The window is positioned to track the WebView widget.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxNativeOverlayProvider : INativeOverlayProvider
{
    private IntPtr _windowHandle;
    private bool _isVisible;

    public IntPtr OverlayHandle => _windowHandle;
    public bool IsVisible => _isVisible;

    public void CreateOverlay(IntPtr parentHandle)
    {
        if (_windowHandle != IntPtr.Zero) return;

        // GTK_WINDOW_POPUP = 1
        _windowHandle = gtk_window_new(1);
        if (_windowHandle == IntPtr.Zero) return;

        // Enable RGBA transparency
        var screen = gtk_widget_get_screen(_windowHandle);
        if (screen != IntPtr.Zero)
        {
            var visual = gdk_screen_get_rgba_visual(screen);
            if (visual != IntPtr.Zero)
            {
                gtk_widget_set_visual(_windowHandle, visual);
            }
        }

        gtk_widget_set_app_paintable(_windowHandle, true);

        // Set transient for parent if provided (keeps overlay above parent)
        if (parentHandle != IntPtr.Zero)
        {
            gtk_window_set_transient_for(_windowHandle, parentHandle);
        }

        // Transparent background via CSS/drawing — set decorated=false
        gtk_window_set_decorated(_windowHandle, false);
        gtk_window_set_skip_taskbar_hint(_windowHandle, true);
        gtk_window_set_skip_pager_hint(_windowHandle, true);
        gtk_window_set_accept_focus(_windowHandle, false);
    }

    public void DestroyOverlay()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            gtk_widget_destroy(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }
        _isVisible = false;
    }

    public void UpdateBounds(double x, double y, double width, double height, double dpiScale)
    {
        if (_windowHandle == IntPtr.Zero) return;

        var px = (int)(x * dpiScale);
        var py = (int)(y * dpiScale);
        var pw = Math.Max(1, (int)(width * dpiScale));
        var ph = Math.Max(1, (int)(height * dpiScale));

        gtk_window_move(_windowHandle, px, py);
        gtk_window_resize(_windowHandle, pw, ph);
    }

    public void Show()
    {
        if (_windowHandle == IntPtr.Zero) return;
        gtk_widget_show_all(_windowHandle);
        _isVisible = true;
    }

    public void Hide()
    {
        if (_windowHandle == IntPtr.Zero) return;
        gtk_widget_hide(_windowHandle);
        _isVisible = false;
    }

    public void Dispose() => DestroyOverlay();

    // ==================== GTK3 P/Invoke ====================

    private const string GtkLib = "libgtk-3.so.0";
    private const string GdkLib = "libgdk-3.so.0";

    [DllImport(GtkLib)]
    private static extern IntPtr gtk_window_new(int type);

    [DllImport(GtkLib)]
    private static extern void gtk_window_move(IntPtr window, int x, int y);

    [DllImport(GtkLib)]
    private static extern void gtk_window_resize(IntPtr window, int width, int height);

    [DllImport(GtkLib)]
    private static extern void gtk_window_set_transient_for(IntPtr window, IntPtr parent);

    [DllImport(GtkLib)]
    private static extern void gtk_window_set_decorated(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool decorated);

    [DllImport(GtkLib)]
    private static extern void gtk_window_set_skip_taskbar_hint(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool skip);

    [DllImport(GtkLib)]
    private static extern void gtk_window_set_skip_pager_hint(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool skip);

    [DllImport(GtkLib)]
    private static extern void gtk_window_set_accept_focus(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool accept);

    [DllImport(GtkLib)]
    private static extern void gtk_widget_show_all(IntPtr widget);

    [DllImport(GtkLib)]
    private static extern void gtk_widget_hide(IntPtr widget);

    [DllImport(GtkLib)]
    private static extern void gtk_widget_destroy(IntPtr widget);

    [DllImport(GtkLib)]
    private static extern void gtk_widget_set_app_paintable(IntPtr widget, [MarshalAs(UnmanagedType.I1)] bool paintable);

    [DllImport(GtkLib)]
    private static extern void gtk_widget_set_visual(IntPtr widget, IntPtr visual);

    [DllImport(GtkLib)]
    private static extern IntPtr gtk_widget_get_screen(IntPtr widget);

    [DllImport(GdkLib)]
    private static extern IntPtr gdk_screen_get_rgba_visual(IntPtr screen);
}
