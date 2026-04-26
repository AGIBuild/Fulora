using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
[Collection(WebKitSmokeHarnessCollection.Name)]
public class WKWebViewSmokeTests
{
    [Fact]
    public async Task Init_with_default_configuration_succeeds()
    {
        if (!OperatingSystem.IsMacOS()) return;

        _ = await WebKitSmokeHarnessRunner.RunAsync("t6-webview-init");
    }

    [Fact]
    public async Task Managed_webview_responds_to_drag_drop_selectors()
    {
        if (!OperatingSystem.IsMacOS()) return;

        _ = await WebKitSmokeHarnessRunner.RunAsync("t21b-webview-drag-selectors");
    }
}
