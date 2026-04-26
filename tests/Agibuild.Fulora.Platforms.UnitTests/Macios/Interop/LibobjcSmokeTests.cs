using Agibuild.Fulora.Platforms.Macios.Interop;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.Interop;

[Trait("Platform", "macOS")]
public class LibobjcSmokeTests
{
    [Fact]
    public void objc_getClass_resolves_NSObject()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var cls = Libobjc.objc_getClass("NSObject");
        Assert.NotEqual(IntPtr.Zero, cls);
    }

    [Fact]
    public void sel_getUid_resolves_alloc()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var sel = Libobjc.sel_getUid("alloc");
        Assert.NotEqual(IntPtr.Zero, sel);
    }
}
