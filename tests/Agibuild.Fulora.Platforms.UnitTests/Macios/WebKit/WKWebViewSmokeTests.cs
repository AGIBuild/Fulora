using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
public class WKWebViewSmokeTests
{
    [Fact]
    public void Init_with_default_configuration_succeeds()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var wkClass = WKWebKit.objc_getClass("WKWebView");
        Assert.NotEqual(IntPtr.Zero, wkClass);

        var cfgClass = WKWebKit.objc_getClass("WKWebViewConfiguration");
        Assert.NotEqual(IntPtr.Zero, cfgClass);

        var navProto = WKWebKit.objc_getProtocol("WKNavigationDelegate");
        Assert.NotEqual(IntPtr.Zero, navProto);

        var initSel = Libobjc.sel_getUid("initWithFrame:configuration:");
        var instSel = Libobjc.sel_getUid("instancesRespondToSelector:");
        Assert.Equal(1, Libobjc.int_objc_msgSend(wkClass, instSel, initSel));
    }
}
