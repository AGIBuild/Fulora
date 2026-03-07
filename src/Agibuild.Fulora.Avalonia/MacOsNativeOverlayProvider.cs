using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Agibuild.Fulora.NativeOverlay;

/// <summary>
/// macOS native overlay using NSPanel via ObjC runtime interop.
/// The panel is borderless, non-activating, and transparent.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacOsNativeOverlayProvider : INativeOverlayProvider
{
    private IntPtr _panelHandle;
    private IntPtr _parentWindowHandle;
    private bool _isVisible;

    public IntPtr OverlayHandle => _panelHandle;
    public bool IsVisible => _isVisible;

    public void CreateOverlay(IntPtr parentHandle)
    {
        if (_panelHandle != IntPtr.Zero) return;
        _parentWindowHandle = parentHandle;

        var nsPanelClass = objc_getClass("NSPanel");
        var allocSel = sel_registerName("alloc");
        var panel = objc_msgSend(nsPanelClass, allocSel);

        // NSWindowStyleMaskBorderless = 0, NSWindowStyleMaskNonactivatingPanel = (1 << 7) = 128
        const ulong styleMask = 128;
        // NSBackingStoreBuffered = 2
        const ulong backingType = 2;
        // initWithContentRect:styleMask:backing:defer:
        var initSel = sel_registerName("initWithContentRect:styleMask:backing:defer:");
        var rect = new NSRect(0, 0, 0, 0);
        panel = objc_msgSend_initPanel(panel, initSel, rect, styleMask, backingType, true);

        if (panel == IntPtr.Zero) return;
        _panelHandle = panel;

        // [panel setOpaque:NO]
        objc_msgSend_bool(panel, sel_registerName("setOpaque:"), false);

        // [panel setBackgroundColor:[NSColor clearColor]]
        var nsColorClass = objc_getClass("NSColor");
        var clearColor = objc_msgSend(nsColorClass, sel_registerName("clearColor"));
        objc_msgSend_ptr(panel, sel_registerName("setBackgroundColor:"), clearColor);

        // [panel setHasShadow:NO]
        objc_msgSend_bool(panel, sel_registerName("setHasShadow:"), false);

        // [panel setLevel:NSFloatingWindowLevel (3)]
        objc_msgSend_long(panel, sel_registerName("setLevel:"), 3);

        // Add as child window of parent
        if (_parentWindowHandle != IntPtr.Zero)
        {
            // [parentWindow addChildWindow:panel ordered:NSWindowAbove(1)]
            objc_msgSend_ptr_long(_parentWindowHandle, sel_registerName("addChildWindow:ordered:"), panel, 1);
        }
    }

    public void DestroyOverlay()
    {
        if (_panelHandle != IntPtr.Zero)
        {
            if (_parentWindowHandle != IntPtr.Zero)
            {
                // [parentWindow removeChildWindow:panel]
                objc_msgSend_ptr(_parentWindowHandle, sel_registerName("removeChildWindow:"), _panelHandle);
            }

            // [panel orderOut:nil]
            objc_msgSend_ptr(_panelHandle, sel_registerName("orderOut:"), IntPtr.Zero);
            // [panel close]
            objc_msgSend(_panelHandle, sel_registerName("close"));
            _panelHandle = IntPtr.Zero;
        }
        _isVisible = false;
    }

    public void UpdateBounds(double x, double y, double width, double height, double dpiScale)
    {
        if (_panelHandle == IntPtr.Zero) return;

        // macOS uses points (not physical pixels) — dpiScale handled by the system.
        // The caller provides logical coordinates; NSPanel frame is in screen coordinates.
        // We set the frame relative to the parent window's content origin.
        var frame = new NSRect(x, y, width, height);
        // [panel setFrame:frame display:YES]
        objc_msgSend_setFrame(_panelHandle, sel_registerName("setFrame:display:"), frame, true);
    }

    public void Show()
    {
        if (_panelHandle == IntPtr.Zero) return;
        // [panel orderFront:nil]
        objc_msgSend_ptr(_panelHandle, sel_registerName("orderFront:"), IntPtr.Zero);
        _isVisible = true;
    }

    public void Hide()
    {
        if (_panelHandle == IntPtr.Zero) return;
        // [panel orderOut:nil]
        objc_msgSend_ptr(_panelHandle, sel_registerName("orderOut:"), IntPtr.Zero);
        _isVisible = false;
    }

    public void Dispose() => DestroyOverlay();

    // ==================== ObjC Runtime Interop ====================

    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect
    {
        public double X, Y, Width, Height;
        public NSRect(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }
    }

    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjCLib)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCLib)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_long(IntPtr receiver, IntPtr selector, long arg);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_ptr_long(IntPtr receiver, IntPtr selector, IntPtr arg1, long arg2);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_initPanel(
        IntPtr receiver, IntPtr selector,
        NSRect contentRect, ulong styleMask, ulong backingType, [MarshalAs(UnmanagedType.I1)] bool defer);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_setFrame(
        IntPtr receiver, IntPtr selector,
        NSRect frame, [MarshalAs(UnmanagedType.I1)] bool display);
}
