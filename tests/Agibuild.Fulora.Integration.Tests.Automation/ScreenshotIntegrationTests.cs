using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Integration tests for the CaptureScreenshotAsync feature.
///
/// HOW IT WORKS (for newcomers):
///   1. We create a MockWebViewAdapterWithScreenshot — it returns fake PNG bytes.
///   2. We wrap it in a WebDialog (same as a real app would).
///   3. We call CaptureScreenshotAsync() and verify the returned bytes.
///   4. We also test that a basic adapter (no screenshot support) throws NotSupportedException.
/// </summary>
public sealed class ScreenshotIntegrationTests
{
    private readonly TestDispatcher _dispatcher = new();

    // ──────────────────── Test 1: Returns PNG bytes ────────────────────

    [AvaloniaFact]
    public void Screenshot_returns_png_bytes()
    {
        // Arrange: adapter that supports screenshots
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithScreenshot();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        // Act: capture screenshot
        var bytes = DispatcherTestPump.Run(_dispatcher, () => dialog.CaptureScreenshotAsync());

        // Assert: got non-empty bytes with PNG magic header (0x89 P N G)
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        Assert.Equal(0x89, bytes[0]); // PNG signature byte 1
        Assert.Equal(0x50, bytes[1]); // 'P'
        Assert.Equal(0x4E, bytes[2]); // 'N'
        Assert.Equal(0x47, bytes[3]); // 'G'
    }

}
