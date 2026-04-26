using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
[Collection(WebKitSmokeHarnessCollection.Name)]
public class WKURLSchemeHandlerTests
{
    [Fact]
    public void Registered_class_responds_to_both_selectors()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var handler = new WKURLSchemeHandlerImpl();
        Assert.True(
            NSObject.RespondsToSelector(handler.Handle, Libobjc.sel_getUid("webView:startURLSchemeTask:")),
            "missing selector: webView:startURLSchemeTask:");
        Assert.True(
            NSObject.RespondsToSelector(handler.Handle, Libobjc.sel_getUid("webView:stopURLSchemeTask:")),
            "missing selector: webView:stopURLSchemeTask:");
    }

    [Fact]
    public async Task Harness_custom_scheme_invokes_StartTask()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        _ = await WebKitSmokeHarnessRunner.RunAsync("t14-url-scheme");
    }
}
