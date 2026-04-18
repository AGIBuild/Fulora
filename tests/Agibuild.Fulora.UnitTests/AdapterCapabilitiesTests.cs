using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Exercises the one-shot capability negotiation performed by
/// <see cref="AdapterCapabilities.From"/>. After the P0 contract consolidation
/// only two facets remain truly optional (<c>DragDrop</c> and
/// <c>AsyncPreloadScript</c>); every other capability is part of the mandatory
/// <see cref="Adapters.Abstractions.IWebViewAdapter"/> surface and is therefore
/// not probed here.
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

        Assert.Null(capabilities.DragDrop);
        Assert.Null(capabilities.AsyncPreloadScript);
    }

    [Fact]
    public void From_detects_drag_drop_capability()
    {
        var adapter = MockWebViewAdapter.CreateWithDragDrop();

        var capabilities = AdapterCapabilities.From(adapter);

        Assert.NotNull(capabilities.DragDrop);
        Assert.Same(adapter, capabilities.DragDrop);
    }

    [Fact]
    public void Record_equality_holds_when_probing_same_adapter_twice()
    {
        var adapter = MockWebViewAdapter.CreateWithDragDrop();

        var first = AdapterCapabilities.From(adapter);
        var second = AdapterCapabilities.From(adapter);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
