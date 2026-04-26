using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
public class WKNavigationDelegateTests
{
    [Fact]
    public void Registered_class_responds_to_all_selectors()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var del = new WKNavigationDelegate();
        foreach (var sel in new[]
        {
            "webView:didFinishNavigation:",
            "webView:decidePolicyForNavigationAction:decisionHandler:",
            "webView:didFailProvisionalNavigation:withError:",
            "webView:didFailNavigation:withError:",
            "webView:decidePolicyForNavigationResponse:decisionHandler:",
            "webView:navigationAction:didBecomeDownload:",
            "webView:navigationResponse:didBecomeDownload:"
        })
        {
            Assert.True(
                NSObject.RespondsToSelector(del.Handle, Libobjc.sel_getUid(sel)),
                $"missing selector: {sel}");
        }
    }
}
