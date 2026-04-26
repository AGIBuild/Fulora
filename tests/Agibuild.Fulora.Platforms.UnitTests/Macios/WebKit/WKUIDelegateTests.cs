using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
[Collection(WebKitSmokeHarnessCollection.Name)]
public class WKUIDelegateTests
{
    [Fact]
    public void Registered_class_responds_to_all_selectors()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var del = new WKUIDelegate();
        foreach (var sel in new[]
        {
            "webView:requestMediaCapturePermissionForOrigin:initiatedByFrame:type:decisionHandler:",
            "webView:runJavaScriptAlertPanelWithMessage:initiatedByFrame:completionHandler:",
            "webView:runJavaScriptConfirmPanelWithMessage:initiatedByFrame:completionHandler:",
            "webView:runJavaScriptTextInputPanelWithPrompt:defaultText:initiatedByFrame:completionHandler:"
        })
        {
            Assert.True(
                NSObject.RespondsToSelector(del.Handle, Libobjc.sel_getUid(sel)),
                $"missing selector: {sel}");
        }
    }

    [Fact]
    public async Task Harness_confirm_panel_dispatches_to_managed_event()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        _ = await WebKitSmokeHarnessRunner.RunAsync("t12-ui-delegate-confirm");
    }
}
