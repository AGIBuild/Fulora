using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Exercises the one-shot capability negotiation performed by <see cref="AdapterCapabilities.From"/>.
/// These tests are the single source of truth for verifying that no optional adapter interface
/// is accidentally dropped when new capabilities are added.
/// </summary>
public sealed class AdapterCapabilitiesTests
{
    [Fact]
    public void From_null_adapter_throws()
    {
        Assert.Throws<ArgumentNullException>(() => AdapterCapabilities.From(null!));
    }

    [Fact]
    public void From_bare_adapter_reports_no_optional_capabilities()
    {
        var adapter = MockWebViewAdapter.Create();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.Null(capabilities.Options);
        Assert.Null(capabilities.CustomScheme);
        Assert.Null(capabilities.Cookie);
        Assert.Null(capabilities.Command);
        Assert.Null(capabilities.Screenshot);
        Assert.Null(capabilities.Print);
        Assert.Null(capabilities.FindInPage);
        Assert.Null(capabilities.Zoom);
        Assert.Null(capabilities.PreloadScript);
        Assert.Null(capabilities.AsyncPreloadScript);
        Assert.Null(capabilities.ContextMenu);
        Assert.Null(capabilities.DragDrop);
        Assert.Null(capabilities.DevTools);
        Assert.Null(capabilities.Download);
        Assert.Null(capabilities.Permission);
        Assert.Null(capabilities.NativeHandleProvider);
    }

    [Fact]
    public void From_detects_cookie_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithCookies();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.Cookie);
        Assert.Same(adapter, capabilities.Cookie);
    }

    [Fact]
    public void From_detects_command_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithCommands();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.Command);
        Assert.Same(adapter, capabilities.Command);
    }

    [Fact]
    public void From_detects_options_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithOptions();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.Options);
    }

    [Fact]
    public void From_detects_custom_scheme_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithCustomSchemes();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.CustomScheme);
    }

    [Fact]
    public void From_detects_native_handle_provider_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithHandle();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.NativeHandleProvider);
    }

    [Fact]
    public void From_detects_download_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithDownload();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.Download);
    }

    [Fact]
    public void From_detects_permission_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithPermission();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.Permission);
    }

    [Fact]
    public void From_detects_screenshot_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithScreenshot();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.Screenshot);
    }

    [Fact]
    public void From_detects_print_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithPrint();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.Print);
    }

    [Fact]
    public void From_detects_find_in_page_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithFind();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.FindInPage);
    }

    [Fact]
    public void From_detects_zoom_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithZoom();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.Zoom);
    }

    [Fact]
    public void From_detects_preload_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithPreload();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.PreloadScript);
    }

    [Fact]
    public void From_detects_context_menu_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithContextMenu();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.ContextMenu);
    }

    [Fact]
    public void From_detects_drag_drop_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithDragDrop();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.DragDrop);
    }

    [Fact]
    public void From_full_adapter_populates_declared_capabilities()
    {
        var adapter = MockWebViewAdapter.CreateFull();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.CustomScheme);
        Assert.NotNull(capabilities.Download);
        Assert.NotNull(capabilities.Permission);
    }

    [Fact]
    public void Record_equality_holds_when_probing_same_adapter_twice()
    {
        var adapter = MockWebViewAdapter.CreateWithCookies();

        var first = AdapterCapabilities.From(adapter);
        var second = AdapterCapabilities.From(adapter);

        // Record struct equality must be value-based (all slots compare as references,
        // so identical adapter should yield equal capabilities snapshots).
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
