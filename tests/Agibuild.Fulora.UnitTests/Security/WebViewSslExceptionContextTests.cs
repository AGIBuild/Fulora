using Agibuild.Fulora.Security;
using Xunit;

namespace Agibuild.Fulora.UnitTests.Security;

public class WebViewSslExceptionContextTests
{
    private static readonly Guid NavId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Legacy_ctor_keeps_existing_behavior()
    {
        var uri = new Uri("https://legacy.example/");
        var ex = new WebViewSslException("explicit message", NavId, uri);

        Assert.Equal("explicit message", ex.Message);
        Assert.Equal(NavId, ex.NavigationId);
        Assert.Equal(uri, ex.RequestUri);
        Assert.Equal(FuloraErrorCodes.NavigationSsl, ex.ErrorCode);
        Assert.Null(ex.Host);
        Assert.Null(ex.ErrorSummary);
        Assert.Equal(0, ex.PlatformRawCode);
        Assert.Null(ex.CertificateSubject);
        Assert.Null(ex.CertificateIssuer);
        Assert.Null(ex.ValidFrom);
        Assert.Null(ex.ValidTo);
    }

    [Fact]
    public void Context_ctor_populates_all_optional_fields_when_present()
    {
        var validFrom = DateTimeOffset.UtcNow.AddDays(-1);
        var validTo = DateTimeOffset.UtcNow.AddDays(60);
        var context = new ServerCertificateErrorContext(
            RequestUri: new Uri("https://bad.example/page"),
            Host: "bad.example",
            ErrorSummary: "CertificateExpired",
            PlatformRawCode: 7,
            CertificateSubject: "CN=bad.example",
            CertificateIssuer: "CN=Test Root CA",
            ValidFrom: validFrom,
            ValidTo: validTo);

        var ex = new WebViewSslException(context, NavId);

        Assert.Equal(NavId, ex.NavigationId);
        Assert.Equal(context.RequestUri, ex.RequestUri);
        Assert.Equal(FuloraErrorCodes.NavigationSsl, ex.ErrorCode);
        Assert.Equal("bad.example", ex.Host);
        Assert.Equal("CertificateExpired", ex.ErrorSummary);
        Assert.Equal(7, ex.PlatformRawCode);
        Assert.Equal("CN=bad.example", ex.CertificateSubject);
        Assert.Equal("CN=Test Root CA", ex.CertificateIssuer);
        Assert.Equal(validFrom, ex.ValidFrom);
        Assert.Equal(validTo, ex.ValidTo);
        Assert.Contains("bad.example", ex.Message);
        Assert.Contains("CertificateExpired", ex.Message);
        Assert.Contains("raw=7", ex.Message);
    }

    [Fact]
    public void Context_ctor_tolerates_missing_optional_certificate_metadata()
    {
        var context = new ServerCertificateErrorContext(
            RequestUri: new Uri("https://partial.example/"),
            Host: "partial.example",
            ErrorSummary: "UnknownRoot",
            PlatformRawCode: 1);

        var ex = new WebViewSslException(context, NavId);

        Assert.Equal("partial.example", ex.Host);
        Assert.Equal("UnknownRoot", ex.ErrorSummary);
        Assert.Equal(1, ex.PlatformRawCode);
        Assert.Null(ex.CertificateSubject);
        Assert.Null(ex.CertificateIssuer);
        Assert.Null(ex.ValidFrom);
        Assert.Null(ex.ValidTo);
    }

    [Fact]
    public void Context_ctor_propagates_inner_exception()
    {
        var inner = new InvalidOperationException("native code");
        var context = new ServerCertificateErrorContext(
            RequestUri: new Uri("https://chain.example/"),
            Host: "chain.example",
            ErrorSummary: "CertificateInvalid",
            PlatformRawCode: 5);

        var ex = new WebViewSslException(context, NavId, inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void Context_ctor_throws_on_null_context()
    {
        Assert.Throws<ArgumentNullException>(() => new WebViewSslException(null!, NavId));
    }
}
