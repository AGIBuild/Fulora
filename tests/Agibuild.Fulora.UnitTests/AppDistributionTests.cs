using System.CommandLine;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Agibuild.Fulora.Cli.Commands;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class AppDistributionTests
{
    private sealed class MockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    [Fact]
    public void VelopackAutoUpdateProvider_GetCurrentVersion_returns_non_null_string()
    {
        var provider = new VelopackAutoUpdateProvider();
        var version = provider.GetCurrentVersion();
        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }

    [Fact]
    public async Task VelopackAutoUpdateProvider_CheckForUpdateAsync_with_empty_feed_returns_null()
    {
        var provider = new VelopackAutoUpdateProvider();
        var options = new AutoUpdateOptions { FeedUrl = "" };
        var result = await provider.CheckForUpdateAsync(options, "1.0.0", TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task VelopackAutoUpdateProvider_CheckForUpdateAsync_with_whitespace_feed_returns_null()
    {
        var provider = new VelopackAutoUpdateProvider();
        var options = new AutoUpdateOptions { FeedUrl = "   " };
        var result = await provider.CheckForUpdateAsync(options, "1.0.0", TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task VelopackAutoUpdateProvider_VerifyPackageAsync_with_non_existent_file_returns_false()
    {
        var provider = new VelopackAutoUpdateProvider();
        var update = new UpdateInfo { Version = "1.0.0", DownloadUrl = "https://example.com/update.nupkg" };
        var result = await provider.VerifyPackageAsync("/nonexistent/path/package.nupkg", update, TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task VelopackAutoUpdateProvider_VerifyPackageAsync_with_empty_path_returns_false()
    {
        var provider = new VelopackAutoUpdateProvider();
        var update = new UpdateInfo { Version = "1.0.0", DownloadUrl = "https://example.com/update.nupkg" };
        var result = await provider.VerifyPackageAsync("", update, TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task VelopackAutoUpdateProvider_VerifyPackageAsync_no_sha256_returns_true()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "test content", TestContext.Current.CancellationToken);
            var provider = new VelopackAutoUpdateProvider();
            var update = new UpdateInfo { Version = "1.0.0", DownloadUrl = "https://example.com/update.nupkg", Sha256 = null };
            var result = await provider.VerifyPackageAsync(tempFile, update, TestContext.Current.CancellationToken);
            Assert.True(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task VelopackAutoUpdateProvider_VerifyPackageAsync_matching_sha256_returns_true()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "test content for SHA verification"u8.ToArray();
            await File.WriteAllBytesAsync(tempFile, content, TestContext.Current.CancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

            var provider = new VelopackAutoUpdateProvider();
            var update = new UpdateInfo { Version = "1.0.0", DownloadUrl = "https://example.com/update.nupkg", Sha256 = hash };
            var result = await provider.VerifyPackageAsync(tempFile, update, TestContext.Current.CancellationToken);
            Assert.True(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task VelopackAutoUpdateProvider_VerifyPackageAsync_mismatched_sha256_returns_false()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "test content", TestContext.Current.CancellationToken);
            var provider = new VelopackAutoUpdateProvider();
            var update = new UpdateInfo { Version = "1.0.0", DownloadUrl = "https://example.com/update.nupkg", Sha256 = "0000000000000000000000000000000000000000000000000000000000000000" };
            var result = await provider.VerifyPackageAsync(tempFile, update, TestContext.Current.CancellationToken);
            Assert.False(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task VelopackAutoUpdateProvider_ApplyUpdateAsync_with_missing_file_is_noop()
    {
        var provider = new VelopackAutoUpdateProvider();
        await provider.ApplyUpdateAsync("", TestContext.Current.CancellationToken);
        await provider.ApplyUpdateAsync("/nonexistent/file.nupkg", TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_format_finds_newer_version()
    {
        var json = """
        {
            "Assets": [
                {
                    "Type": "Full",
                    "Version": "2.0.0",
                    "FileName": "app-2.0.0.nupkg",
                    "Size": 1024,
                    "SHA256": "abc123"
                }
            ]
        }
        """;
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result!.Version);
        Assert.Equal("https://feed.example.com/app-2.0.0.nupkg", result.DownloadUrl);
        Assert.Equal(1024, result.SizeBytes);
        Assert.Equal("abc123", result.Sha256);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_format_skips_delta_assets()
    {
        var json = """
        {
            "Assets": [
                {
                    "Type": "Delta",
                    "Version": "3.0.0",
                    "FileName": "delta-3.0.0.nupkg"
                },
                {
                    "Type": "Full",
                    "Version": "2.0.0",
                    "FileName": "full-2.0.0.nupkg"
                }
            ]
        }
        """;
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result!.Version);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_format_returns_null_when_up_to_date()
    {
        var json = """
        {
            "Assets": [
                {
                    "Type": "Full",
                    "Version": "1.0.0",
                    "FileName": "app-1.0.0.nupkg"
                }
            ]
        }
        """;
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_format_picks_highest_version()
    {
        var json = """
        {
            "Assets": [
                { "Type": "Full", "Version": "1.5.0", "FileName": "v150.nupkg" },
                { "Type": "Full", "Version": "2.0.0", "FileName": "v200.nupkg" },
                { "Type": "Full", "Version": "1.8.0", "FileName": "v180.nupkg" }
            ]
        }
        """;
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result!.Version);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_simple_format_finds_newer_version()
    {
        var json = """
        {
            "version": "2.0.0",
            "downloadUrl": "https://cdn.example.com/app-2.0.0.exe",
            "sha256": "abc123",
            "sizeBytes": 2048,
            "releaseNotes": "Bug fixes",
            "isMandatory": true
        }
        """;
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result!.Version);
        Assert.Equal("https://cdn.example.com/app-2.0.0.exe", result.DownloadUrl);
        Assert.Equal("abc123", result.Sha256);
        Assert.Equal(2048, result.SizeBytes);
        Assert.Equal("Bug fixes", result.ReleaseNotes);
        Assert.True(result.IsMandatory);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_simple_format_returns_null_when_up_to_date()
    {
        var json = """{ "version": "1.0.0", "downloadUrl": "https://example.com/app.exe" }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_simple_format_download_url_variant()
    {
        var json = """{ "version": "2.0.0", "download_url": "https://example.com/app.exe" }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal("https://example.com/app.exe", result!.DownloadUrl);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_invalid_json_returns_null()
    {
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate("not json", "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_without_assets_or_version_returns_null()
    {
        var json = """{ "channel": "stable" }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_empty_assets_returns_null()
    {
        var json = """{ "Assets": [] }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_simple_no_download_url_returns_null()
    {
        var json = """{ "version": "2.0.0" }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_asset_with_http_filename()
    {
        var json = """
        {
            "Assets": [
                { "Type": "Full", "Version": "3.0.0", "FileName": "https://cdn.example.com/custom.nupkg" }
            ]
        }
        """;
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal("https://cdn.example.com/custom.nupkg", result!.DownloadUrl);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_asset_missing_filename_skipped()
    {
        var json = """{ "Assets": [{ "Type": "Full", "Version": "2.0.0" }] }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_asset_invalid_version_skipped()
    {
        var json = """{ "Assets": [{ "Type": "Full", "Version": "not-a-version", "FileName": "app.nupkg" }] }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_asset_missing_type_is_treated_as_full()
    {
        var json = """{ "Assets": [{ "Version": "2.0.0", "FileName": "app.nupkg" }] }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal("https://feed.example.com/app.nupkg", result!.DownloadUrl);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_asset_missing_version_property_skipped()
    {
        var json = """{ "Assets": [{ "Type": "Full", "FileName": "app.nupkg" }] }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_simple_invalid_feed_version_returns_null()
    {
        var json = """{ "version": "not-a-version", "downloadUrl": "https://example.com/app.exe" }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_simple_size_via_size_property()
    {
        var json = """{ "version": "2.0.0", "downloadUrl": "https://example.com/app.exe", "size": 4096 }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal(4096, result!.SizeBytes);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_assets_not_array_falls_through_to_simple()
    {
        var json = """{ "Assets": "not-an-array", "version": "2.0.0", "downloadUrl": "https://example.com/app.exe" }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result!.Version);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_older_version_in_array_skipped()
    {
        var json = """
        {
            "Assets": [
                { "Type": "Full", "Version": "2.0.0", "FileName": "v200.nupkg" },
                { "Type": "Full", "Version": "1.5.0", "FileName": "v150.nupkg" }
            ]
        }
        """;
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result!.Version);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_simple_empty_version_returns_null()
    {
        var json = """{ "version": "", "downloadUrl": "https://example.com/app.exe" }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "1.0.0");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_velopack_invalid_current_version_still_returns_latest()
    {
        var json = """{ "Assets": [{ "Type": "Full", "Version": "2.0.0", "FileName": "app.nupkg" }] }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "invalid-version");
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result!.Version);
    }

    [Fact]
    public void ParseFeedAndFindUpdate_simple_invalid_current_version_returns_null()
    {
        var json = """{ "version": "2.0.0", "downloadUrl": "https://example.com/app.exe" }""";
        var result = VelopackAutoUpdateProvider.ParseFeedAndFindUpdate(json, "https://feed.example.com/", "invalid-version");
        Assert.Null(result);
    }

    [Fact]
    public void GetBaseUrl_extracts_directory_from_feed_url()
    {
        Assert.Equal("https://example.com/releases/", VelopackAutoUpdateProvider.GetBaseUrl("https://example.com/releases/feed.json"));
        Assert.Equal("https://example.com/", VelopackAutoUpdateProvider.GetBaseUrl("https://example.com/feed.json"));
    }

    [Fact]
    public void GetBaseUrl_supports_non_hierarchical_uri()
    {
        Assert.Equal("urn:updates:feed/", VelopackAutoUpdateProvider.GetBaseUrl("urn:updates:feed"));
    }

    [Fact]
    public void PackageCommand_creates_valid_command_with_expected_options()
    {
        var command = PackageCommand.Create();
        Assert.NotNull(command);
        Assert.Equal("package", command.Name);

        var optionNames = command.Options.Select(o => o.Name).ToHashSet();
        Assert.Contains("--project", optionNames);
        Assert.Contains("--runtime", optionNames);
        Assert.Contains("--version", optionNames);
        Assert.Contains("--output", optionNames);
        Assert.Contains("--icon", optionNames);
        Assert.Contains("--sign-params", optionNames);
        Assert.Contains("--notarize", optionNames);
        Assert.Contains("--channel", optionNames);
    }

    [Fact]
    public void PackageCommand_creates_valid_command_with_profile_option()
    {
        var command = PackageCommand.Create();

        var optionNames = command.Options.Select(o => o.Name).ToHashSet();
        Assert.Contains("--profile", optionNames);
    }

    [Fact]
    public void ResolveProfile_desktop_public_sets_stable_channel_defaults()
    {
        var profile = PackageProfileDefaults.Resolve("desktop-public");

        Assert.Equal("stable", profile.Channel);
        Assert.False(profile.Notarize);
    }

    [Fact]
    public async Task CheckForUpdateAsync_with_velopack_feed_returns_update()
    {
        var feedJson = """{ "Assets": [{ "Type": "Full", "Version": "2.0.0", "FileName": "app-2.0.0.nupkg", "Size": 1024, "SHA256": "abc" }] }""";
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(feedJson) });
        using var httpClient = new HttpClient(handler);
        var provider = new VelopackAutoUpdateProvider(httpClient);
        var options = new AutoUpdateOptions { FeedUrl = "https://feed.example.com/releases.json" };
        var result = await provider.CheckForUpdateAsync(options, "1.0.0", TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result!.Version);
    }

    [Fact]
    public async Task CheckForUpdateAsync_with_simple_feed_returns_update()
    {
        var feedJson = """{ "version": "3.0.0", "downloadUrl": "https://cdn.example.com/app.exe", "sha256": "def" }""";
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(feedJson) });
        using var httpClient = new HttpClient(handler);
        var provider = new VelopackAutoUpdateProvider(httpClient);
        var options = new AutoUpdateOptions { FeedUrl = "https://feed.example.com/update.json" };
        var result = await provider.CheckForUpdateAsync(options, "1.0.0", TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("3.0.0", result!.Version);
    }

    [Fact]
    public async Task CheckForUpdateAsync_with_headers_applies_custom_headers()
    {
        HttpRequestMessage? capturedRequest = null;
        var feedJson = """{ "version": "1.0.0", "downloadUrl": "https://x.com/a.exe" }""";
        var handler = new MockHandler(req => { capturedRequest = req; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(feedJson) }; });
        using var httpClient = new HttpClient(handler);
        var provider = new VelopackAutoUpdateProvider(httpClient);
        var options = new AutoUpdateOptions
        {
            FeedUrl = "https://feed.example.com/update.json",
            Headers = new Dictionary<string, string> { ["X-Auth"] = "token123" }
        };
        await provider.CheckForUpdateAsync(options, "1.0.0", TestContext.Current.CancellationToken);
        Assert.NotNull(capturedRequest);
        Assert.Contains("token123", capturedRequest!.Headers.GetValues("X-Auth"));
    }

    [Fact]
    public async Task DownloadUpdateAsync_downloads_and_reports_progress()
    {
        var content = Encoding.UTF8.GetBytes("fake update content");
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content) { Headers = { ContentLength = content.Length } }
        });
        using var httpClient = new HttpClient(handler);
        var provider = new VelopackAutoUpdateProvider(httpClient);
        var update = new UpdateInfo { Version = "2.0.0", DownloadUrl = "https://cdn.example.com/app-2.0.0.nupkg", SizeBytes = content.Length };
        var options = new AutoUpdateOptions { FeedUrl = "https://feed.example.com/" };
        var progressReports = new List<UpdateDownloadProgress>();
        var packagePath = await provider.DownloadUpdateAsync(update, options, p => progressReports.Add(p), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(packagePath));
        Assert.NotEmpty(progressReports);
        Assert.Equal(content.Length, progressReports[^1].BytesDownloaded);
        Assert.NotNull(progressReports[^1].ProgressPercent);

        File.Delete(packagePath);
        var dir = Path.GetDirectoryName(packagePath);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public async Task DownloadUpdateAsync_with_no_content_length_uses_update_size()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
        using var httpClient = new HttpClient(handler);
        var provider = new VelopackAutoUpdateProvider(httpClient);
        var update = new UpdateInfo { Version = "2.0.0", DownloadUrl = "https://cdn.example.com/app.nupkg", SizeBytes = 4096 };
        var options = new AutoUpdateOptions { FeedUrl = "https://feed.example.com/" };
        var progressReports = new List<UpdateDownloadProgress>();
        var packagePath = await provider.DownloadUpdateAsync(update, options, p => progressReports.Add(p), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(packagePath));
        Assert.NotEmpty(progressReports);

        File.Delete(packagePath);
        var dir = Path.GetDirectoryName(packagePath);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public async Task DownloadUpdateAsync_with_empty_filename_uses_default()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("x"u8.ToArray())
        });
        using var httpClient = new HttpClient(handler);
        var provider = new VelopackAutoUpdateProvider(httpClient);
        var update = new UpdateInfo { Version = "2.0.0", DownloadUrl = "https://cdn.example.com/" };
        var options = new AutoUpdateOptions { FeedUrl = "https://feed.example.com/" };
        var packagePath = await provider.DownloadUpdateAsync(update, options, ct: TestContext.Current.CancellationToken);

        Assert.Contains("update-2.0.0.nupkg", packagePath);
        File.Delete(packagePath);
        var dir = Path.GetDirectoryName(packagePath);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void PackageCommand_validates_missing_project_argument()
    {
        var root = new RootCommand("test");
        root.Subcommands.Add(PackageCommand.Create());
        var parseResult = root.Parse("package");
        var exitCode = parseResult.Invoke();
        Assert.NotEqual(0, exitCode);
    }
}
