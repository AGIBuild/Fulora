using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Integration tests for SPA asset hot update install → activate → rollback lifecycle.
/// Exercises the full SpaAssetHotUpdateService → SpaHostingService integration.
/// </summary>
public sealed class SpaAssetHotUpdateIntegrationTests : IDisposable
{
    private readonly string _root;

    public SpaAssetHotUpdateIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fulora-it-hot-update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Install_activate_serve_rollback_full_lifecycle()
    {
        var service = new SpaAssetHotUpdateService(_root);
        using var rsa = RSA.Create(2048);

        var v1 = CreatePackage(new Dictionary<string, string> { ["index.html"] = "<html>v1</html>" });
        var s1 = rsa.SignData(v1, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var installV1 = await service.InstallSignedPackageAsync(
            new MemoryStream(v1), "1.0.0", s1, rsa, TestContext.Current.CancellationToken);
        Assert.True(installV1.Succeeded);

        var activateV1 = service.ActivateVersion("1.0.0");
        Assert.True(activateV1.Succeeded);

        using (var hosting = new SpaHostingService(
            new SpaHostingOptions
            {
                Scheme = "app",
                Host = "localhost",
                FallbackDocument = "index.html",
                ActiveAssetDirectoryProvider = service.GetActiveAssetDirectory
            },
            NullLogger.Instance))
        {
            var request = new WebResourceRequestedEventArgs(new Uri("app://localhost/index.html"), "GET");
            hosting.TryHandle(request);
            Assert.Equal(200, request.ResponseStatusCode);
            using var reader = new StreamReader(request.ResponseBody!, Encoding.UTF8, leaveOpen: false);
            Assert.Equal("<html>v1</html>", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }

        var v2 = CreatePackage(new Dictionary<string, string> { ["index.html"] = "<html>v2</html>" });
        var s2 = rsa.SignData(v2, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        await service.InstallSignedPackageAsync(
            new MemoryStream(v2), "2.0.0", s2, rsa, TestContext.Current.CancellationToken);
        service.ActivateVersion("2.0.0");

        using (var hosting = new SpaHostingService(
            new SpaHostingOptions
            {
                Scheme = "app",
                Host = "localhost",
                FallbackDocument = "index.html",
                ActiveAssetDirectoryProvider = service.GetActiveAssetDirectory
            },
            NullLogger.Instance))
        {
            var request = new WebResourceRequestedEventArgs(new Uri("app://localhost/index.html"), "GET");
            hosting.TryHandle(request);
            using var reader = new StreamReader(request.ResponseBody!, Encoding.UTF8, leaveOpen: false);
            Assert.Equal("<html>v2</html>", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }

        var rollback = service.Rollback();
        Assert.True(rollback.Succeeded);
        Assert.Equal("1.0.0", rollback.Version);

        using (var hosting = new SpaHostingService(
            new SpaHostingOptions
            {
                Scheme = "app",
                Host = "localhost",
                FallbackDocument = "index.html",
                ActiveAssetDirectoryProvider = service.GetActiveAssetDirectory
            },
            NullLogger.Instance))
        {
            var request = new WebResourceRequestedEventArgs(new Uri("app://localhost/index.html"), "GET");
            hosting.TryHandle(request);
            using var reader = new StreamReader(request.ResponseBody!, Encoding.UTF8, leaveOpen: false);
            Assert.Equal("<html>v1</html>", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Invalid_signature_blocks_installation()
    {
        var service = new SpaAssetHotUpdateService(_root);
        using var rsa = RSA.Create(2048);

        var package = CreatePackage(new Dictionary<string, string> { ["index.html"] = "<html>bad</html>" });
        var fakeSignature = new byte[256];
        Random.Shared.NextBytes(fakeSignature);

        var result = await service.InstallSignedPackageAsync(
            new MemoryStream(package), "bad-1.0", fakeSignature, rsa, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("signature-invalid", result.Code);
        Assert.Null(service.GetActiveAssetDirectory());
    }

    [Fact]
    public void Activate_missing_version_returns_failure()
    {
        var service = new SpaAssetHotUpdateService(_root);
        var result = service.ActivateVersion("nonexistent");

        Assert.False(result.Succeeded);
        Assert.Equal("version-not-installed", result.Code);
    }

    [Fact]
    public void Rollback_without_previous_returns_failure()
    {
        var service = new SpaAssetHotUpdateService(_root);
        var result = service.Rollback();

        Assert.False(result.Succeeded);
        Assert.Equal("no-previous-version", result.Code);
    }

    private static byte[] CreatePackage(IReadOnlyDictionary<string, string> files)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Fastest);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(file.Value);
            }
        }
        return ms.ToArray();
    }
}
