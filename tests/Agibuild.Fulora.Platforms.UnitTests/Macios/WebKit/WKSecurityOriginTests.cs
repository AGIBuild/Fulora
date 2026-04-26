using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

public sealed class WKSecurityOriginTests
{
    [Theory]
    [InlineData("https", "example.com", 443, "https://example.com:443")]
    [InlineData("http", "example.com", 80, "http://example.com:80")]
    [InlineData("custom", "example.com", 0, "custom://example.com")]
    [InlineData("", "example.com", 0, "://example.com")]
    [InlineData("file", "", 0, "file://")]
    public void FormatOrigin_matches_legacy_shim_serialization(string scheme, string host, long port, string expected)
    {
        var origin = WKSecurityOrigin.FormatOrigin(scheme, host, port);

        Assert.Equal(expected, origin);
    }

    [Fact]
    public void FormatOrigin_returns_null_when_protocol_and_host_are_empty()
    {
        var origin = WKSecurityOrigin.FormatOrigin("", "", 0);

        Assert.Null(origin);
    }
}
