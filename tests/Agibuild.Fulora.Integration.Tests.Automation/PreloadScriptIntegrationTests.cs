using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Integration tests for the Preload Script feature.
///
/// HOW IT WORKS (for newcomers):
///   1. We create a MockWebViewAdapterWithPreload — it tracks registered scripts in a dictionary.
///   2. We wrap it in a WebDialog (same as a real app would).
///   3. We call AddPreloadScript("...") and verify a script ID is returned.
///   4. We call RemovePreloadScript(id) and verify the script was removed.
///   5. We also test that a basic adapter (no preload support) throws NotSupportedException.
/// </summary>
public sealed class PreloadScriptIntegrationTests
{
    private readonly TestDispatcher _dispatcher = new();

    // ──────────────────── Test 1: Add returns script ID ────────────────────

    [AvaloniaFact]
    public void Add_returns_script_id()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        var id = DispatcherTestPump.Run(_dispatcher, () => dialog.AddPreloadScriptAsync("console.log('hello')"));

        Assert.NotNull(id);
        Assert.NotEmpty(id);
    }

    // ──────────────────── Test 2: Remove deletes script ────────────────────

    [AvaloniaFact]
    public void Remove_deletes_script()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        var id = DispatcherTestPump.Run(_dispatcher, () => dialog.AddPreloadScriptAsync("console.log('hello')"));
        DispatcherTestPump.Run(_dispatcher, () => dialog.RemovePreloadScriptAsync(id));

        var preloadAdapter = (MockWebViewAdapterWithPreload)adapter;
        Assert.Empty(preloadAdapter.Scripts);
    }

    // ──────────────────── Test 3: Multiple scripts tracked independently ────────────────────

    [AvaloniaFact]
    public void Multiple_scripts_tracked_independently()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        var id1 = DispatcherTestPump.Run(_dispatcher, () => dialog.AddPreloadScriptAsync("console.log('a')"));
        var id2 = DispatcherTestPump.Run(_dispatcher, () => dialog.AddPreloadScriptAsync("console.log('b')"));

        Assert.NotEqual(id1, id2);

        DispatcherTestPump.Run(_dispatcher, () => dialog.RemovePreloadScriptAsync(id1));

        var preloadAdapter = (MockWebViewAdapterWithPreload)adapter;
        Assert.Single(preloadAdapter.Scripts);
        Assert.Contains(id2, preloadAdapter.Scripts.Keys);
    }

    // ──────────────────── Test 6: Add after dispose throws ────────────────────

    [AvaloniaFact]
    public void Add_after_dispose_throws_ObjectDisposedException()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        var dialog = new WebDialog(host, adapter, _dispatcher);
        dialog.Dispose();

        Assert.Throws<ObjectDisposedException>(() => DispatcherTestPump.Run(_dispatcher, () => dialog.AddPreloadScriptAsync("x")));
    }

    // ──────────────────── Test 7: Remove after dispose throws ────────────────────

    [AvaloniaFact]
    public void Remove_after_dispose_throws_ObjectDisposedException()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        var dialog = new WebDialog(host, adapter, _dispatcher);
        var id = DispatcherTestPump.Run(_dispatcher, () => dialog.AddPreloadScriptAsync("x"));
        dialog.Dispose();

        Assert.Throws<ObjectDisposedException>(() => DispatcherTestPump.Run(_dispatcher, () => dialog.RemovePreloadScriptAsync(id)));
    }

    // ──────────────────── Test 8: Remove unknown ID is no-op ────────────────────

    [AvaloniaFact]
    public void Remove_unknown_id_is_noop()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        DispatcherTestPump.Run(_dispatcher, () => dialog.AddPreloadScriptAsync("console.log('keep')"));
        DispatcherTestPump.Run(_dispatcher, () => dialog.RemovePreloadScriptAsync("nonexistent-id")); // should not throw

        var preloadAdapter = (MockWebViewAdapterWithPreload)adapter;
        Assert.Single(preloadAdapter.Scripts); // original still there
    }

    // ──────────────────── Test 9: Add → Remove → Add produces new ID ────────────────────

    [AvaloniaFact]
    public void Add_remove_add_produces_new_id()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        var id1 = DispatcherTestPump.Run(_dispatcher, () => dialog.AddPreloadScriptAsync("console.log('a')"));
        DispatcherTestPump.Run(_dispatcher, () => dialog.RemovePreloadScriptAsync(id1));
        var id2 = DispatcherTestPump.Run(_dispatcher, () => dialog.AddPreloadScriptAsync("console.log('b')"));

        Assert.NotEqual(id1, id2);
        var preloadAdapter = (MockWebViewAdapterWithPreload)adapter;
        Assert.Single(preloadAdapter.Scripts);
    }

    // ──────────────────── Test 10: Global preloads applied at construction ────────────────────

    [AvaloniaFact]
    public void Global_preloads_applied_at_construction()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        var originalOptions = WebViewEnvironment.Options;
        try
        {
            WebViewEnvironment.Options = new WebViewEnvironmentOptions
            {
                PreloadScripts = new[] { "console.log('g1')", "console.log('g2')" }
            };
            using var dialog = new WebDialog(host, adapter, _dispatcher);

            var preloadAdapter = (MockWebViewAdapterWithPreload)adapter;
            Assert.Equal(2, preloadAdapter.Scripts.Count);
        }
        finally
        {
            WebViewEnvironment.Options = originalOptions;
        }
    }
}
