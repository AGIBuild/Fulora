using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
[Collection(WebKitSmokeHarnessCollection.Name)]
public class WKUserContentControllerTests
{
    [Fact]
    public async Task Construct_add_user_script_remove_all_succeeds()
    {
        if (!OperatingSystem.IsMacOS()) return;

        _ = await WebKitSmokeHarnessRunner.RunAsync("t8-user-content-controller");
    }
}
