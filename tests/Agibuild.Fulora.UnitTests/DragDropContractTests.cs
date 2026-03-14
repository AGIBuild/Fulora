using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class DragDropContractTests
{
    [Fact]
    public void WebViewCore_with_drag_drop_adapter_fires_DragEntered_when_adapter_raises()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithDragDrop();
        using var core = new WebViewCore(adapter, dispatcher);

        DragEventArgs? received = null;
        core.DragEntered += (_, e) => received = e;

        var args = new DragEventArgs
        {
            Payload = new DragDropPayload { Text = "test" },
            AllowedEffects = DragDropEffects.Copy,
            X = 10,
            Y = 20
        };
        ((MockWebViewAdapterWithDragDrop)adapter).RaiseDragEntered(args);

        Assert.NotNull(received);
        Assert.Equal("test", received!.Payload.Text);
        Assert.Equal(10, received.X);
        Assert.Equal(20, received.Y);
    }

    [Fact]
    public void WebViewCore_with_drag_drop_adapter_fires_DropCompleted_when_adapter_raises()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithDragDrop();
        using var core = new WebViewCore(adapter, dispatcher);

        DropEventArgs? received = null;
        core.DropCompleted += (_, e) => received = e;

        var args = new DropEventArgs
        {
            Payload = new DragDropPayload { Uri = "https://example.com" },
            Effect = DragDropEffects.Link,
            X = 50,
            Y = 60
        };
        ((MockWebViewAdapterWithDragDrop)adapter).RaiseDropCompleted(args);

        Assert.NotNull(received);
        Assert.Equal("https://example.com", received!.Payload.Uri);
        Assert.Equal(DragDropEffects.Link, received.Effect);
        Assert.Equal(50, received.X);
        Assert.Equal(60, received.Y);
    }

    [Fact]
    public void WebViewCore_without_drag_drop_adapter_does_not_crash()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        core.DragEntered += (_, _) => { };
        core.DragOver += (_, _) => { };
        core.DragLeft += (_, _) => { };
        core.DropCompleted += (_, _) => { };
    }

    [Fact]
    public void WebViewCore_drag_events_fire_without_subscribers()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithDragDrop();
        using var core = new WebViewCore(adapter, dispatcher);
        var dd = (MockWebViewAdapterWithDragDrop)adapter;

        dd.RaiseDragOver(new DragEventArgs
        {
            Payload = new DragDropPayload { Text = "t" },
            AllowedEffects = DragDropEffects.Copy
        });
        dd.RaiseDragLeft();
    }

    [Fact]
    public void DragDropPayload_with_files_stores_all_fields()
    {
        var files = new[] { new FileDropInfo("/tmp/a.txt", "text/plain", 1024) };
        var payload = new DragDropPayload { Files = files, Text = "hello", Html = "<b>hi</b>", Uri = "https://a.com" };
        Assert.Single(payload.Files!);
        Assert.Equal("/tmp/a.txt", payload.Files![0].Path);
        Assert.Equal("text/plain", payload.Files[0].MimeType);
        Assert.Equal(1024, payload.Files[0].Size);
        Assert.Equal("hello", payload.Text);
        Assert.Equal("<b>hi</b>", payload.Html);
        Assert.Equal("https://a.com", payload.Uri);
    }

    [Fact]
    public void DragDropEffects_flags_combine_correctly()
    {
        var effects = DragDropEffects.Copy | DragDropEffects.Move;
        Assert.True(effects.HasFlag(DragDropEffects.Copy));
        Assert.True(effects.HasFlag(DragDropEffects.Move));
        Assert.False(effects.HasFlag(DragDropEffects.Link));
        Assert.Equal(DragDropEffects.All, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    [Fact]
    public void DragEventArgs_allows_setting_effect()
    {
        var args = new DragEventArgs
        {
            Payload = new DragDropPayload(),
            AllowedEffects = DragDropEffects.All,
            X = 1,
            Y = 2
        };
        Assert.Equal(DragDropEffects.Copy, args.Effect);
        args.Effect = DragDropEffects.Move;
        Assert.Equal(DragDropEffects.Move, args.Effect);
    }

    [Fact]
    public void FileDropInfo_stores_path_and_optional_metadata()
    {
        var info = new FileDropInfo("/file.txt");
        Assert.Equal("/file.txt", info.Path);
        Assert.Null(info.MimeType);
        Assert.Null(info.Size);

        var full = new FileDropInfo("/file.pdf", "application/pdf", 2048);
        Assert.Equal("application/pdf", full.MimeType);
        Assert.Equal(2048, full.Size);
    }

    [Fact]
    public async Task DragDropBridgeService_returns_null_initially()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithDragDrop();
        using var core = new WebViewCore(adapter, dispatcher);
        var service = new DragDropBridgeService(core);

        var result = await service.GetLastDropPayloadAsync(TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task DragDropBridgeService_captures_drop_payload()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithDragDrop();
        using var core = new WebViewCore(adapter, dispatcher);
        var service = new DragDropBridgeService(core);

        var payload = new DragDropPayload { Text = "dropped text" };
        ((MockWebViewAdapterWithDragDrop)adapter).RaiseDropCompleted(new DropEventArgs
        {
            Payload = payload,
            Effect = DragDropEffects.Copy
        });

        var result = await service.GetLastDropPayloadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("dropped text", result!.Text);
    }

    [Fact]
    public async Task DragDropBridgeService_reports_supported()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithDragDrop();
        using var core = new WebViewCore(adapter, dispatcher);
        var service = new DragDropBridgeService(core);

        Assert.True(await service.IsDragDropSupportedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DragDropBridgeService_reports_unsupported_when_adapter_has_no_drag_drop()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);
        var service = new DragDropBridgeService(core);

        Assert.False(await service.IsDragDropSupportedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DragDropBridgeService_delivers_multiple_drops()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithDragDrop();
        using var core = new WebViewCore(adapter, dispatcher);
        var service = new DragDropBridgeService(core);
        var dd = (MockWebViewAdapterWithDragDrop)adapter;

        // First drop
        dd.RaiseDropCompleted(new DropEventArgs
        {
            Payload = new DragDropPayload { Text = "first" },
            Effect = DragDropEffects.Copy
        });
        var r1 = await service.GetLastDropPayloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("first", r1?.Text);

        // Second drop replaces first
        dd.RaiseDropCompleted(new DropEventArgs
        {
            Payload = new DragDropPayload { Text = "second" },
            Effect = DragDropEffects.Move
        });
        var r2 = await service.GetLastDropPayloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("second", r2?.Text);
    }

    [Fact]
    public async Task DragDropBridgeService_captures_file_payload()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithDragDrop();
        using var core = new WebViewCore(adapter, dispatcher);
        var service = new DragDropBridgeService(core);
        var dd = (MockWebViewAdapterWithDragDrop)adapter;

        var payload = new DragDropPayload
        {
            Files = new List<FileDropInfo>
            {
                new FileDropInfo("/tmp/test.txt", "text/plain", 1024),
                new FileDropInfo("/tmp/image.png", "image/png", 2048)
            }
        };
        dd.RaiseDropCompleted(new DropEventArgs { Payload = payload, Effect = DragDropEffects.Copy });

        var result = await service.GetLastDropPayloadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result?.Files);
        Assert.Equal(2, result!.Files!.Count);
        Assert.Equal("/tmp/test.txt", result.Files[0].Path);
        Assert.Equal(2048, result.Files[1].Size);
    }

    [Fact]
    public async Task DragDropBridgeService_captures_html_and_uri()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithDragDrop();
        using var core = new WebViewCore(adapter, dispatcher);
        var service = new DragDropBridgeService(core);
        var dd = (MockWebViewAdapterWithDragDrop)adapter;

        dd.RaiseDropCompleted(new DropEventArgs
        {
            Payload = new DragDropPayload
            {
                Html = "<b>bold</b>",
                Uri = "https://example.com"
            },
            Effect = DragDropEffects.Link
        });

        var result = await service.GetLastDropPayloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("<b>bold</b>", result?.Html);
        Assert.Equal("https://example.com", result?.Uri);
    }

    [Fact]
    public void DragDropEffects_bitwise_or_combines()
    {
        var combined = DragDropEffects.Copy | DragDropEffects.Move;
        Assert.True(combined.HasFlag(DragDropEffects.Copy));
        Assert.True(combined.HasFlag(DragDropEffects.Move));
        Assert.False(combined.HasFlag(DragDropEffects.Link));
    }

    [Fact]
    public void DropEventArgs_holds_all_properties()
    {
        var args = new DropEventArgs
        {
            Payload = new DragDropPayload { Text = "hello" },
            Effect = DragDropEffects.Move,
            X = 100.5,
            Y = 200.3
        };
        Assert.Equal("hello", args.Payload.Text);
        Assert.Equal(DragDropEffects.Move, args.Effect);
        Assert.Equal(100.5, args.X);
        Assert.Equal(200.3, args.Y);
    }
}
