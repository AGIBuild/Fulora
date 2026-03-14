using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Integration tests for the Context Menu Interception feature.
///
/// HOW IT WORKS (for newcomers):
///   1. We create a MockWebViewAdapterWithContextMenu — it can simulate a context menu event.
///   2. We wrap it in a WebDialog (same as a real app would).
///   3. The adapter fires ContextMenuRequested with hit-test info.
///   4. We verify the event arrives at the WebDialog level with correct fields.
///   5. We test that setting Handled=true propagates back to suppress the native menu.
/// </summary>
public sealed class ContextMenuIntegrationTests
{
    private readonly TestDispatcher _dispatcher = new();

    // ──────────────────── Test 1: Event fires with hit-test info ────────────────────

    [AvaloniaFact]
    public void Event_fires_with_hitTest_info()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        ContextMenuRequestedEventArgs? received = null;
        dialog.ContextMenuRequested += (_, e) => received = e;

        var args = new ContextMenuRequestedEventArgs
        {
            X = 150,
            Y = 250,
            LinkUri = new Uri("https://example.com/link"),
            SelectionText = "selected text",
            MediaType = ContextMenuMediaType.Image,
            MediaSourceUri = new Uri("https://example.com/img.png"),
            IsEditable = false
        };
        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(args);

        Assert.NotNull(received);
        Assert.Equal(150, received!.X);
        Assert.Equal(250, received.Y);
        Assert.Equal("https://example.com/link", received.LinkUri?.ToString());
        Assert.Equal("selected text", received.SelectionText);
        Assert.Equal(ContextMenuMediaType.Image, received.MediaType);
        Assert.False(received.IsEditable);
    }

    // ──────────────────── Test 2: Handled flag suppresses native menu ────────────────────

    [AvaloniaFact]
    public void Handled_flag_suppresses_native_menu()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        dialog.ContextMenuRequested += (_, e) => e.Handled = true;

        var args = new ContextMenuRequestedEventArgs { X = 10, Y = 20 };
        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(args);

        // The Handled flag should be true after the consumer sets it
        Assert.True(args.Handled);
    }

    // ──────────────────── Test 3: No handler — Handled stays false ────────────────────

    [AvaloniaFact]
    public void No_handler_Handled_stays_false()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        var args = new ContextMenuRequestedEventArgs { X = 10, Y = 20 };
        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(args);

        // Without handler, Handled stays false — native menu shows
        Assert.False(args.Handled);
    }

    // ──────────────────── Test 4: Without IContextMenuAdapter — no event ────────────────────

    [AvaloniaFact]
    public void Without_adapter_no_event_fires()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create(); // basic — no context menu support
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        ContextMenuRequestedEventArgs? received = null;
        dialog.ContextMenuRequested += (_, e) => received = e;

        // Can't trigger anything — should stay null
        Assert.Null(received);
    }

    // ──────────────────── Test 5: Event not raised after dispose ────────────────────

    [AvaloniaFact]
    public void Event_not_raised_after_dispose()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        var dialog = new WebDialog(host, adapter, _dispatcher);

        ContextMenuRequestedEventArgs? received = null;
        dialog.ContextMenuRequested += (_, e) => received = e;

        dialog.Dispose();

        // Fire after dispose — should NOT reach handler
        var args = new ContextMenuRequestedEventArgs { X = 10, Y = 20 };
        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(args);

        Assert.Null(received);
    }

    // ──────────────────── Test 6: Unsubscribe stops events ────────────────────

    [AvaloniaFact]
    public void Unsubscribe_stops_events()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        int callCount = 0;
        void Handler(object? s, ContextMenuRequestedEventArgs e) => callCount++;
        dialog.ContextMenuRequested += Handler;

        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(new() { X = 1, Y = 2 });
        Assert.Equal(1, callCount);

        dialog.ContextMenuRequested -= Handler;
        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(new() { X = 3, Y = 4 });
        Assert.Equal(1, callCount); // should not increment
    }

    // ──────────────────── Test 7: Multiple handlers all invoked ────────────────────

    [AvaloniaFact]
    public void Multiple_handlers_all_invoked()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        int handler1Count = 0, handler2Count = 0;
        dialog.ContextMenuRequested += (_, _) => handler1Count++;
        dialog.ContextMenuRequested += (_, _) => handler2Count++;

        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(new() { X = 1, Y = 2 });

        Assert.Equal(1, handler1Count);
        Assert.Equal(1, handler2Count);
    }

    // ──────────────────── Test 8: Minimal event args (all-null optionals) ────────────────────

    [AvaloniaFact]
    public void Minimal_event_args_work()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        ContextMenuRequestedEventArgs? received = null;
        dialog.ContextMenuRequested += (_, e) => received = e;

        var args = new ContextMenuRequestedEventArgs { X = 0, Y = 0 };
        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(args);

        Assert.NotNull(received);
        Assert.Null(received!.LinkUri);
        Assert.Null(received.SelectionText);
        Assert.Null(received.MediaSourceUri);
        Assert.Equal(ContextMenuMediaType.None, received.MediaType);
        Assert.False(received.IsEditable);
        Assert.False(received.Handled);
    }
}
