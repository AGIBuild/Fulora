using System.Reflection;
using Agibuild.Fulora.Platforms.Macios.Interop.Security;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.Security;

[Trait("Platform", "macOS")]
public class X509MetadataExtractorTests
{
    [Fact]
    public void Extract_returns_subject_issuer_validity_for_known_cert()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var cert = SecCertificate.FromDer(ReadCertificateResource());
        var (subject, issuer, notBefore, notAfter) = X509MetadataExtractor.Extract(cert);

        Assert.Contains("CN=fulora-test", subject, StringComparison.Ordinal);
        Assert.Contains("CN=fulora-test", issuer, StringComparison.Ordinal);
        Assert.True(notAfter > notBefore);
    }

    private static byte[] ReadCertificateResource()
    {
        var assembly = typeof(X509MetadataExtractorTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".Macios.Security.Resources.test-cert.cer", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
