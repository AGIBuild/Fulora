using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Agibuild.Fulora;

/// <summary>
/// Auto-update provider that works with a generic update feed URL.
/// Compatible with Velopack-style releases.{channel}.json feeds and simple custom JSON feeds.
/// </summary>
public sealed class VelopackAutoUpdateProvider : IAutoUpdatePlatformProvider
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly HttpClient _httpClient;

    /// <summary>Creates a provider using the default shared HttpClient.</summary>
    public VelopackAutoUpdateProvider() => _httpClient = SharedHttpClient;

    /// <summary>Creates a provider with a custom HttpClient (for testing).</summary>
    internal VelopackAutoUpdateProvider(HttpClient httpClient) => _httpClient = httpClient;

    /// <inheritdoc />
    public string GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckForUpdateAsync(AutoUpdateOptions options, string currentVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.FeedUrl))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, options.FeedUrl);
        ApplyHeaders(request, options.Headers);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var baseUrl = GetBaseUrl(options.FeedUrl);

        return ParseFeedAndFindUpdate(json, baseUrl, currentVersion);
    }

    /// <inheritdoc />
    public async Task<string> DownloadUpdateAsync(UpdateInfo update, AutoUpdateOptions options, Action<UpdateDownloadProgress>? onProgress = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
        ApplyHeaders(request, options.Headers);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? update.SizeBytes;
        var tempDir = Path.Combine(Path.GetTempPath(), "FuloraUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Path.GetFileName(new Uri(update.DownloadUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName))
            fileName = $"update-{update.Version}.nupkg";
        var packagePath = Path.Combine(tempDir, fileName);

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fileStream = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

        var buffer = new byte[81920];
        long bytesDownloaded = 0;

        while (true)
        {
            var read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;

            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            bytesDownloaded += read;

            onProgress?.Invoke(new UpdateDownloadProgress
            {
                BytesDownloaded = bytesDownloaded,
                TotalBytes = totalBytes,
                ProgressPercent = totalBytes.HasValue && totalBytes.Value > 0
                    ? Math.Min(100, (double)bytesDownloaded / totalBytes.Value * 100)
                    : null,
            });
        }

        return packagePath;
    }

    /// <inheritdoc />
    public Task<bool> VerifyPackageAsync(string packagePath, UpdateInfo update, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            return Task.FromResult(false);

        if (string.IsNullOrWhiteSpace(update.Sha256))
            return Task.FromResult(true);

        try
        {
            using var stream = File.OpenRead(packagePath);
            var hash = SHA256.HashData(stream);
            var computed = Convert.ToHexString(hash).ToLowerInvariant();
            var expected = update.Sha256.Trim().ToLowerInvariant().Replace("-", "");
            return Task.FromResult(computed == expected);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task ApplyUpdateAsync(string packagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            return Task.CompletedTask;

        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo("open", packagePath) { UseShellExecute = false });
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo(packagePath) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private static void ApplyHeaders(HttpRequestMessage request, IDictionary<string, string>? headers)
    {
        if (headers is null)
            return;
        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);
    }

    internal static string GetBaseUrl(string feedUrl)
    {
        var uri = new Uri(feedUrl);
        var path = uri.AbsolutePath;
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash >= 0)
            return new Uri(uri, path[..(lastSlash + 1)]).ToString();
        return uri.ToString().TrimEnd('/') + "/";
    }

    internal static UpdateInfo? ParseFeedAndFindUpdate(string json, string baseUrl, string currentVersion)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                return ParseVelopackAssets(assets, baseUrl, currentVersion);

            if (root.TryGetProperty("version", out var versionEl))
                return ParseSimpleFeed(root, baseUrl, currentVersion, versionEl.GetString());
        }
        catch (JsonException)
        {
            // Fall through to null
        }

        return null;
    }

    private static UpdateInfo? ParseVelopackAssets(JsonElement assets, string baseUrl, string currentVersion)
    {
        UpdateInfo? latest = null;
        Version? latestVer = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var type = asset.TryGetProperty("Type", out var t) ? t.GetString() : null;
            if (string.Equals(type, "Delta", StringComparison.OrdinalIgnoreCase))
                continue;

            var versionStr = asset.TryGetProperty("Version", out var v) ? v.GetString() : null;
            if (string.IsNullOrEmpty(versionStr))
                continue;

            if (!Version.TryParse(versionStr, out var version))
                continue;

            if (latestVer is not null && version <= latestVer)
                continue;

            var fileName = asset.TryGetProperty("FileName", out var fn) ? fn.GetString() : null;
            if (string.IsNullOrEmpty(fileName))
                continue;

            var downloadUrl = fileName.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : baseUrl.TrimEnd('/') + "/" + fileName.TrimStart('/');

            var size = asset.TryGetProperty("Size", out var sz) && sz.TryGetInt64(out var n) ? n : (long?)null;
            var sha256 = asset.TryGetProperty("SHA256", out var s256) ? s256.GetString() : null;

            latestVer = version;
            latest = new UpdateInfo
            {
                Version = versionStr,
                DownloadUrl = downloadUrl,
                SizeBytes = size,
                Sha256 = sha256,
            };
        }

        if (latest is null)
            return null;

        if (!Version.TryParse(currentVersion, out var current) || latestVer > current)
            return latest;

        return null;
    }

    private static UpdateInfo? ParseSimpleFeed(JsonElement root, string baseUrl, string currentVersion, string? versionStr)
    {
        if (string.IsNullOrEmpty(versionStr))
            return null;

        if (!Version.TryParse(versionStr, out var feedVersion))
            return null;

        if (!Version.TryParse(currentVersion, out var current) || feedVersion <= current)
            return null;

        var downloadUrl = root.TryGetProperty("downloadUrl", out var du)
            ? du.GetString()
            : root.TryGetProperty("download_url", out var du2)
                ? du2.GetString()
                : null;

        if (string.IsNullOrEmpty(downloadUrl))
            return null;

        var sha256 = root.TryGetProperty("sha256", out var sh) ? sh.GetString() : null;
        long? sizeBytes = null;
        if (root.TryGetProperty("sizeBytes", out var sb) && sb.TryGetInt64(out var n))
            sizeBytes = n;
        else if (root.TryGetProperty("size", out var s2) && s2.TryGetInt64(out var n2))
            sizeBytes = n2;

        var releaseNotes = root.TryGetProperty("releaseNotes", out var rn) ? rn.GetString() : null;
        var isMandatory = root.TryGetProperty("isMandatory", out var im) && im.GetBoolean();

        return new UpdateInfo
        {
            Version = versionStr,
            DownloadUrl = downloadUrl,
            ReleaseNotes = releaseNotes,
            SizeBytes = sizeBytes,
            IsMandatory = isMandatory,
            Sha256 = sha256,
        };
    }
}
