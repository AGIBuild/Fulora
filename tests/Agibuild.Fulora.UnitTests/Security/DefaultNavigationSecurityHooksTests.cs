using Agibuild.Fulora.Security;
using Xunit;

namespace Agibuild.Fulora.UnitTests.Security;

public class DefaultNavigationSecurityHooksTests
{
    private static ServerCertificateErrorContext SampleContext() => new(
        RequestUri: new Uri("https://example.invalid/"),
        Host: "example.invalid",
        ErrorSummary: "CertificateUntrusted",
        PlatformRawCode: 3,
        CertificateSubject: "CN=example.invalid",
        CertificateIssuer: "CN=Test CA",
        ValidFrom: DateTimeOffset.UtcNow.AddDays(-1),
        ValidTo: DateTimeOffset.UtcNow.AddDays(30));

    [Fact]
    public void Instance_returns_same_singleton_across_calls()
    {
        Assert.Same(DefaultNavigationSecurityHooks.Instance, DefaultNavigationSecurityHooks.Instance);
    }

    [Fact]
    public void OnServerCertificateError_returns_Reject()
    {
        var decision = DefaultNavigationSecurityHooks.Instance.OnServerCertificateError(SampleContext());
        Assert.Equal(NavigationSecurityDecision.Reject, decision);
    }

    [Fact]
    public void OnServerCertificateError_throws_on_null_context()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DefaultNavigationSecurityHooks.Instance.OnServerCertificateError(null!));
    }

    [Fact]
    public async Task OnServerCertificateError_is_safe_to_invoke_concurrently()
    {
        // Many concurrent callers must always observe the single allowed value.
        const int callers = 32;
        const int iterations = 200;
        var results = new NavigationSecurityDecision[callers * iterations];

        await Task.WhenAll(Enumerable.Range(0, callers).Select(c => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                results[(c * iterations) + i] =
                    DefaultNavigationSecurityHooks.Instance.OnServerCertificateError(SampleContext());
            }
        })));

        Assert.All(results, d => Assert.Equal(NavigationSecurityDecision.Reject, d));
    }
}
