using Agibuild.Fulora.Platforms.Macios.Interop;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.Interop;

[Trait("Platform", "macOS")]
public class NSObjectLifecycleTests
{
    // NOTE: NSString.Create(string?) constructs the wrapper with owns:false (the
    // returned NSString is autoreleased by the runtime), so NSObject.Dispose
    // intentionally does NOT send `release` for this path. This test therefore
    // smokes "construction yields a live handle and dispose is exception-safe".
    // owns:true / release-on-dispose semantics belong to types that retain
    // ownership (e.g. NSData.FromBytes) and are exercised by their own tests.
    [Fact]
    public void Construct_and_dispose_roundtrip_succeeds()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var s = NSString.Create("x")!;
        Assert.NotEqual(IntPtr.Zero, s.Handle);
    }
}
