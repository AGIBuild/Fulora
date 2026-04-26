using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
[Collection(WebKitSmokeHarnessCollection.Name)]
public class WKHTTPCookieStoreTests
{
    [Fact]
    public async Task Harness_t9_cookie_store_roundtrip_succeeds()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        _ = await WebKitSmokeHarnessRunner.RunAsync("t9-cookie-store-roundtrip");
    }
}
