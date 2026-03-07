using Agibuild.Fulora;
using Agibuild.Fulora.NativeOverlay;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class PlatformFunctionalGapTests
{
    // ==================== Android Preload Script Injection ====================

    [Fact]
    public void MockPreloadAdapter_stores_and_returns_scripts()
    {
        var adapter = MockWebViewAdapter.CreateWithPreload();
        var preload = (MockWebViewAdapterWithPreload)adapter;

        var id1 = preload.AddPreloadScript("console.log('a')");
        var id2 = preload.AddPreloadScript("console.log('b')");

        Assert.NotNull(id1);
        Assert.NotNull(id2);
        Assert.NotEqual(id1, id2);
        Assert.Equal(2, preload.Scripts.Count);
    }

    [Fact]
    public void MockPreloadAdapter_removes_script()
    {
        var adapter = MockWebViewAdapter.CreateWithPreload();
        var preload = (MockWebViewAdapterWithPreload)adapter;

        var id = preload.AddPreloadScript("console.log('a')");
        preload.RemovePreloadScript(id);

        Assert.Empty(preload.Scripts);
    }

    // ==================== DragDropPayload construction ====================

    [Fact]
    public void DragDropPayload_with_files_and_text()
    {
        var payload = new DragDropPayload
        {
            Text = "hello",
            Files = new List<FileDropInfo>
            {
                new("test.pdf", "application/pdf", 1024),
                new("image.png", "image/png", 2048)
            }
        };

        Assert.Equal("hello", payload.Text);
        Assert.Equal(2, payload.Files.Count);
        Assert.Equal("test.pdf", payload.Files[0].Path);
        Assert.Equal(2048, payload.Files[1].Size);
    }

    [Fact]
    public void DragEventArgs_carries_payload_and_coordinates()
    {
        var args = new DragEventArgs
        {
            Payload = new DragDropPayload { Uri = "https://example.com" },
            AllowedEffects = DragDropEffects.Copy | DragDropEffects.Link,
            Effect = DragDropEffects.Copy,
            X = 100.5,
            Y = 200.3
        };

        Assert.Equal("https://example.com", args.Payload.Uri);
        Assert.Equal(100.5, args.X);
        Assert.Equal(200.3, args.Y);
    }

    [Fact]
    public void DropEventArgs_carries_payload_and_coordinates()
    {
        var args = new DropEventArgs
        {
            Payload = new DragDropPayload { Text = "dropped text" },
            Effect = DragDropEffects.Move,
            X = 50,
            Y = 75
        };

        Assert.Equal("dropped text", args.Payload.Text);
        Assert.Equal(DragDropEffects.Move, args.Effect);
    }

    // ==================== INativeOverlayProvider contract ====================

    [Fact]
    public void NativeOverlayProviderFactory_returns_non_null_on_current_platform()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return;

        var provider = NativeOverlayProviderFactory.Create();
        Assert.NotNull(provider);
        Assert.IsAssignableFrom<INativeOverlayProvider>(provider);
        provider.Dispose();
    }

    [Fact]
    public void NativeOverlayProvider_initial_state()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return;

        using var provider = NativeOverlayProviderFactory.Create();

        Assert.False(provider.IsVisible);
        Assert.Equal(IntPtr.Zero, provider.OverlayHandle);
    }

    // ==================== GTK PDF printing contract ====================

    [Fact]
    public void PdfPrintOptions_default_values()
    {
        var options = new PdfPrintOptions();

        Assert.NotNull(options);
    }
}
