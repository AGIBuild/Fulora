using Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.Security;

[Trait("Platform", "macOS")]
[Trait("Category", "Integration")]
[Collection(WebKitSmokeHarnessCollection.Name)]
public class ServerTrustChainTests
{
    [Fact]
    public async Task SelfSigned_trust_raises_event_with_full_certificate_metadata()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        _ = await WebKitSmokeHarnessRunner.RunAsync("t17-server-trust");
    }
}
