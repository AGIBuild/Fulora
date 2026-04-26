using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
public class WKDownloadDelegateTests
{
    [Fact]
    public void Registered_class_responds_to_all_three_selectors_when_supported()
    {
        if (!OperatingSystem.IsMacOS() || !OperatingSystem.IsMacOSVersionAtLeast(11, 3))
        {
            return;
        }

        using var del = new WKDownloadDelegate();
        Assert.True(
            NSObject.RespondsToSelector(
                del.Handle,
                Libobjc.sel_getUid("download:decideDestinationUsingResponse:suggestedFilename:completionHandler:")),
            "missing selector: download:decideDestinationUsingResponse:suggestedFilename:completionHandler:");
        Assert.True(
            NSObject.RespondsToSelector(del.Handle, Libobjc.sel_getUid("download:didFailWithError:resumeData:")),
            "missing selector: download:didFailWithError:resumeData:");
        Assert.True(
            NSObject.RespondsToSelector(del.Handle, Libobjc.sel_getUid("downloadDidFinish:")),
            "missing selector: downloadDidFinish:");
    }

    [Fact]
    public void Construction_throws_PlatformNotSupportedException_below_macOS_11_3()
    {
        if (!OperatingSystem.IsMacOS() || OperatingSystem.IsMacOSVersionAtLeast(11, 3))
        {
            return;
        }

        Assert.Throws<PlatformNotSupportedException>(() => new WKDownloadDelegate());
    }
}
