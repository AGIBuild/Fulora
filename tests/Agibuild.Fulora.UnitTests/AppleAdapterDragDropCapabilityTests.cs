using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Adapters.MacOS;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class AppleAdapterDragDropCapabilityTests
{
    [Fact]
    public void MacOS_adapter_exposes_drag_drop_capability()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var adapter = new MacOSWebViewAdapter();

        Assert.IsAssignableFrom<IDragDropAdapter>(adapter);
    }
}
