using Agibuild.Fulora.Platforms.Macios.Interop;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.Interop;

[Trait("Platform", "macOS")]
public class NSStringRoundtripTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("中文 unicode 🚀")]
    public void Roundtrip_preserves_value(string input)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var s = NSString.Create(input)!;
        Assert.Equal(input, s.GetString());
    }
}
