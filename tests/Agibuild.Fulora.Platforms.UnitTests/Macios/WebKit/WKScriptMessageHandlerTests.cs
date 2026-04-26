using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
[Collection(WebKitSmokeHarnessCollection.Name)]
public class WKScriptMessageHandlerTests
{
    [Fact]
    public void Registered_class_responds_to_selector()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var handler = new WKScriptMessageHandler();
        Assert.True(
            NSObject.RespondsToSelector(
                handler.Handle,
                Libobjc.sel_getUid("userContentController:didReceiveScriptMessage:")),
            "missing selector: userContentController:didReceiveScriptMessage:");
    }

    [Fact]
    public async Task Harness_post_message_dispatches_to_managed_event()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        _ = await WebKitSmokeHarnessRunner.RunAsync("t13-script-message");
    }
}
