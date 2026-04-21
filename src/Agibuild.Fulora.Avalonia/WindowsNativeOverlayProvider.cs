using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Agibuild.Fulora.NativeOverlay;

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsNativeOverlayProvider : INativeOverlayProvider
{
    private IntPtr _overlayHwnd;
    private bool _isVisible;

    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_POPUP = unchecked((int)0x80000000);

    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int SWP_HIDEWINDOW = 0x0080;

    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_HIDE = 0;

    private const byte LWA_ALPHA = 0x02;

    public IntPtr OverlayHandle => _overlayHwnd;
    public bool IsVisible => _isVisible;

    public void CreateOverlay(IntPtr parentHandle)
    {
        if (_overlayHwnd != IntPtr.Zero) return;

        var exStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        _overlayHwnd = CreateWindowEx(
            exStyle,
            "Static", // Standard window class
            "",
            WS_POPUP,
            0, 0, 0, 0,
            parentHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_overlayHwnd != IntPtr.Zero)
        {
            SetLayeredWindowAttributes(_overlayHwnd, 0, 255, LWA_ALPHA);
        }
    }

    public void DestroyOverlay()
    {
        if (_overlayHwnd != IntPtr.Zero)
        {
            DestroyWindow(_overlayHwnd);
            _overlayHwnd = IntPtr.Zero;
        }
        _isVisible = false;
    }

    public void UpdateBounds(double x, double y, double width, double height, double dpiScale)
    {
        if (_overlayHwnd == IntPtr.Zero) return;

        var px = (int)(x * dpiScale);
        var py = (int)(y * dpiScale);
        var pw = (int)(width * dpiScale);
        var ph = (int)(height * dpiScale);

        SetWindowPos(_overlayHwnd, IntPtr.Zero, px, py, pw, ph, SWP_NOACTIVATE | SWP_NOZORDER);
    }

    public void Show()
    {
        if (_overlayHwnd == IntPtr.Zero) return;
        ShowWindow(_overlayHwnd, SW_SHOWNOACTIVATE);
        _isVisible = true;
    }

    public void Hide()
    {
        if (_overlayHwnd == IntPtr.Zero) return;
        ShowWindow(_overlayHwnd, SW_HIDE);
        _isVisible = false;
    }

    public void Dispose() => DestroyOverlay();

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
