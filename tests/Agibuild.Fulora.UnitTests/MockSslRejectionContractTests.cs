using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Security;
using Agibuild.Fulora.Testing;
using Agibuild.Fulora.UnitTests.TestDoubles;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Runs <see cref="AdapterSslRejectionContract"/> against
/// <see cref="MockWebViewAdapter"/> as the reference implementation.
/// </summary>
public sealed class MockSslRejectionContractTests
{
    private static IWebViewAdapter MockTriggerFactory(
        out Action<Uri, string?, string?, DateTimeOffset?, DateTimeOffset?, string?, int> triggerSslError)
    {
        var mock = MockWebViewAdapter.Create();
        triggerSslError = mock.TriggerServerCertificateError;
        return mock;
    }

    [Fact]
    public Task ServerCertificateError_raises_NavigationCompleted_with_Failure_status()
        => AdapterSslRejectionContract.ServerCertificateError_raises_NavigationCompleted_with_Failure_status(
            MockTriggerFactory);

    [Fact]
    public Task ServerCertificateError_exception_is_WebViewSslException_with_host_and_summary()
        => AdapterSslRejectionContract.ServerCertificateError_exception_is_WebViewSslException_with_host_and_summary(
            MockTriggerFactory);

    [Fact]
    public Task ServerCertificateError_always_cancels_navigation()
        => AdapterSslRejectionContract.ServerCertificateError_always_cancels_navigation(MockTriggerFactory);

    [Fact]
    public Task ServerCertificateError_propagates_certificate_metadata_when_supplied()
        => AdapterSslRejectionContract.ServerCertificateError_propagates_certificate_metadata_when_supplied(
            MockTriggerFactory);

    [Fact]
    public Task PlatformProvidesCertificateMetadata_when_supported_requires_metadata()
        => AdapterSslRejectionContract.PlatformProvidesCertificateMetadata_when_supported(
            MockTriggerFactory,
            platformSupportsMetadata: true);

    [Fact]
    public Task PlatformProvidesCertificateMetadata_when_not_supported_allows_null_metadata()
        => AdapterSslRejectionContract.PlatformProvidesCertificateMetadata_when_supported(
            MockTriggerFactory,
            platformSupportsMetadata: false);

    [Fact]
    public void Hook_is_invoked_with_expected_context()
    {
        var hook = new RecordingNavigationSecurityHooks();
        var mock = MockWebViewAdapter.CreateWithSecurityHook(hook);
        var uri = new Uri("https://hook-context.example/resource");

        mock.TriggerServerCertificateError(uri, errorSummary: "hook-summary", platformRawCode: 7);

        var ctx = Assert.Single(hook.Received);
        Assert.Equal(uri, ctx.RequestUri);
        Assert.Equal(uri.Host, ctx.Host);
    }
}
