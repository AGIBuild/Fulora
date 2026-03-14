using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Integration tests for the Find-in-Page feature.
///
/// HOW IT WORKS (for newcomers):
///   1. We create a MockWebViewAdapterWithFind — it returns a preset FindInPageEventArgs.
///   2. We wrap it in a WebDialog (same as a real app would).
///   3. We call FindInPageAsync("text") and verify the returned result.
///   4. We call StopFindInPage() and verify the adapter was notified.
///   5. We also test that a basic adapter (no find support) throws NotSupportedException.
/// </summary>
public sealed class FindInPageIntegrationTests
{
    private readonly TestDispatcher _dispatcher = new();

    // ──────────────────── Test 1: Find returns match result ────────────────────

    [AvaloniaFact]
    public void Find_returns_match_result()
    {
        // Arrange: adapter that supports find-in-page
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        // Act
        var result = DispatcherTestPump.Run(_dispatcher, () => dialog.FindInPageAsync("hello"));

        // Assert
        Assert.Equal(0, result.ActiveMatchIndex);
        Assert.Equal(3, result.TotalMatches);
    }

    // ──────────────────── Test 2: Find passes options to adapter ────────────────────

    [AvaloniaFact]
    public void Find_passes_options_to_adapter()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        var opts = new FindInPageOptions { CaseSensitive = true, Forward = false };
        DispatcherTestPump.Run(_dispatcher, () => dialog.FindInPageAsync("test", opts));

        var findAdapter = (MockWebViewAdapterWithFind)adapter;
        Assert.Equal("test", findAdapter.LastSearchText);
        Assert.True(findAdapter.LastOptions?.CaseSensitive);
        Assert.False(findAdapter.LastOptions?.Forward);
    }

    // ──────────────────── Test 3: StopFind notifies adapter ────────────────────

    [AvaloniaFact]
    public void StopFind_notifies_adapter()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        DispatcherTestPump.Run(_dispatcher, () => dialog.StopFindInPageAsync(false));

        var findAdapter = (MockWebViewAdapterWithFind)adapter;
        Assert.True(findAdapter.StopFindCalled);
        Assert.False(findAdapter.LastClearHighlights);
    }

    // ──────────────────── Test 4: Find without adapter throws ────────────────────

    [AvaloniaFact]
    public void Find_without_adapter_throws_NotSupportedException()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create(); // basic — no find support
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        Assert.Throws<NotSupportedException>(() => DispatcherTestPump.Run(_dispatcher, () => dialog.FindInPageAsync("x")));
    }

    // ──────────────────── Test 5: StopFind without adapter throws ────────────────────

    [AvaloniaFact]
    public void StopFind_without_adapter_throws_NotSupportedException()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        Assert.Throws<NotSupportedException>(() => DispatcherTestPump.Run(_dispatcher, () => dialog.StopFindInPageAsync()));
    }

    // ──────────────────── Test 6: Find after dispose throws ────────────────────

    [AvaloniaFact]
    public void Find_after_dispose_throws_ObjectDisposedException()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithFind();
        var dialog = new WebDialog(host, adapter, _dispatcher);
        dialog.Dispose();

        Assert.Throws<ObjectDisposedException>(() => DispatcherTestPump.Run(_dispatcher, () => dialog.FindInPageAsync("test")));
    }

    // ──────────────────── Test 7: StopFind after dispose throws ────────────────────

    [AvaloniaFact]
    public void StopFind_after_dispose_throws_ObjectDisposedException()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithFind();
        var dialog = new WebDialog(host, adapter, _dispatcher);
        dialog.Dispose();

        Assert.Throws<ObjectDisposedException>(() => DispatcherTestPump.Run(_dispatcher, () => dialog.StopFindInPageAsync()));
    }

    // ──────────────────── Test 8: Find with null text throws ArgumentException ────────────────────

    [AvaloniaFact]
    public void Find_with_null_text_throws_ArgumentException()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        Assert.Throws<ArgumentException>(() => DispatcherTestPump.Run(_dispatcher, () => dialog.FindInPageAsync(null!)));
    }

    // ──────────────────── Test 9: Find with empty text throws ArgumentException ────────────────────

    [AvaloniaFact]
    public void Find_with_empty_text_throws_ArgumentException()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        Assert.Throws<ArgumentException>(() => DispatcherTestPump.Run(_dispatcher, () => dialog.FindInPageAsync("")));
    }

    // ──────────────────── Test 10: Find → Stop → Find again works ────────────────────

    [AvaloniaFact]
    public void Find_stop_find_again_works()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        DispatcherTestPump.Run(_dispatcher, () => dialog.FindInPageAsync("first"));
        DispatcherTestPump.Run(_dispatcher, () => dialog.StopFindInPageAsync());
        var result = DispatcherTestPump.Run(_dispatcher, () => dialog.FindInPageAsync("second"));

        var findAdapter = (MockWebViewAdapterWithFind)adapter;
        Assert.Equal("second", findAdapter.LastSearchText);
        Assert.Equal(3, result.TotalMatches);
    }
}
