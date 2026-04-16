using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Handles WebResourceRequested events to serve embedded resources or proxy to a dev server.
/// Implements SPA fallback, MIME type detection, caching headers, and optional bridge script injection.
/// </summary>
internal sealed class SpaHostingService : IDisposable
{
    private readonly SpaHostingOptions _options;
    private readonly ILogger _logger;
    private readonly HttpClient? _devProxy;
    private bool _disposed;

    // MIME type mappings for common web assets.
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".mjs"] = "application/javascript",
        [".json"] = "application/json",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".eot"] = "application/vnd.ms-fontobject",
        [".otf"] = "font/otf",
        [".wasm"] = "application/wasm",
        [".map"] = "application/json",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".xml"] = "application/xml",
        [".txt"] = "text/plain",
        [".pdf"] = "application/pdf",
    };

    public SpaHostingService(SpaHostingOptions options, ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var hasEmbeddedPrefix = options.EmbeddedResourcePrefix is not null;
        var hasEmbeddedAssembly = options.ResourceAssembly is not null;
        if (hasEmbeddedPrefix != hasEmbeddedAssembly)
        {
            throw new ArgumentException(
                "EmbeddedResourcePrefix and ResourceAssembly must be configured together.",
                nameof(options));
        }

        var hasEmbedded = hasEmbeddedPrefix && hasEmbeddedAssembly;
        var hasExternalAssets = options.ActiveAssetDirectoryProvider is not null;

        if (options.DevServerUrl is not null)
        {
            _devProxy = new HttpClient { BaseAddress = new Uri(options.DevServerUrl.TrimEnd('/') + "/") };
            _logger.LogDebug("SPA: dev proxy mode → {DevServer}", options.DevServerUrl);
        }
        else if (hasEmbedded)
        {
            _logger.LogDebug("SPA: embedded mode, prefix={Prefix}, assembly={Assembly}",
                options.EmbeddedResourcePrefix, options.ResourceAssembly!.GetName().Name);
        }
        else if (hasExternalAssets)
        {
            _logger.LogDebug("SPA: external active-asset mode enabled");
        }
        else
        {
            throw new ArgumentException(
                "One hosting source must be configured: DevServerUrl, embedded resources, or ActiveAssetDirectoryProvider.",
                nameof(options));
        }
    }

    /// <summary>
    /// Returns the <see cref="CustomSchemeRegistration"/> to register with the WebView environment.
    /// </summary>
    public CustomSchemeRegistration GetSchemeRegistration() => new()
    {
        SchemeName = _options.Scheme,
        HasAuthorityComponent = true,
        TreatAsSecure = true,
    };

    /// <summary>
    /// Handles a WebResourceRequested event. Returns true if the request was handled.
    /// </summary>
    public bool TryHandle(WebResourceRequestedEventArgs e)
    {
        if (_disposed || e.Handled) return false;

        var uri = e.RequestUri;
        if (uri is null) return false;
        if (!uri.Scheme.Equals(_options.Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!uri.Host.Equals(_options.Host, StringComparison.OrdinalIgnoreCase)) return false;

        var path = uri.AbsolutePath.TrimStart('/');

        if (_options.ServiceWorker is { } sw &&
            path.Equals(sw.ScriptPath.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
        {
            return ServeServiceWorkerScript(e, sw);
        }

        // SPA fallback: if path has no file extension, serve the fallback document.
        if (string.IsNullOrEmpty(path) || !Path.HasExtension(path))
        {
            path = _options.FallbackDocument;
            _logger.LogDebug("SPA: fallback → {Path}", path);
        }

        bool served;
        if (_devProxy is not null)
        {
            served = HandleViaDevProxy(e, path);
        }
        else if (HandleViaExternalAssets(e, path))
        {
            served = true;
        }
        else if (_options.EmbeddedResourcePrefix is null || _options.ResourceAssembly is null)
        {
            e.ResponseStatusCode = 404;
            e.ResponseBody = new MemoryStream(Encoding.UTF8.GetBytes("Not Found"));
            e.ResponseContentType = "text/plain";
            e.Handled = true;
            served = true;
        }
        else
        {
            served = HandleViaEmbeddedResource(e, path);
        }

        if (served && _options.ServiceWorker is { } swOptions &&
            e.ResponseContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true &&
            e.ResponseBody is not null)
        {
            e.ResponseBody = InjectSwRegistrationScript(e.ResponseBody, swOptions);
        }

        return served;
    }

    // ==================== Embedded resource serving ====================

    private bool HandleViaExternalAssets(WebResourceRequestedEventArgs e, string path)
    {
        if (_options.ActiveAssetDirectoryProvider is null)
            return false;

        var activeDirectory = _options.ActiveAssetDirectoryProvider();
        if (string.IsNullOrWhiteSpace(activeDirectory))
            return false;

        var rootDirectory = Path.GetFullPath(activeDirectory);
        if (!Directory.Exists(rootDirectory))
        {
            e.ResponseStatusCode = 404;
            e.ResponseBody = new MemoryStream(Encoding.UTF8.GetBytes("Active asset directory not found"));
            e.ResponseContentType = "text/plain";
            e.Handled = true;
            return true;
        }

        var normalizedPath = path.Replace('/', Path.DirectorySeparatorChar);
        var candidatePath = Path.GetFullPath(Path.Combine(rootDirectory, normalizedPath));
        if (!candidatePath.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            e.ResponseStatusCode = 400;
            e.ResponseBody = new MemoryStream(Encoding.UTF8.GetBytes("Invalid asset path"));
            e.ResponseContentType = "text/plain";
            e.Handled = true;
            return true;
        }

        var filePath = candidatePath;
        if (!File.Exists(filePath) && path != _options.FallbackDocument)
        {
            filePath = Path.Combine(rootDirectory, _options.FallbackDocument);
        }

        if (!File.Exists(filePath))
        {
            e.ResponseStatusCode = 404;
            e.ResponseBody = new MemoryStream(Encoding.UTF8.GetBytes("Not Found"));
            e.ResponseContentType = "text/plain";
            e.Handled = true;
            return true;
        }

        var ext = Path.GetExtension(filePath);
        e.ResponseBody = File.OpenRead(filePath);
        e.ResponseContentType = GetMimeType(ext);
        e.ResponseStatusCode = 200;
        e.Handled = true;
        e.ResponseHeaders ??= new Dictionary<string, string>();
        e.ResponseHeaders["Cache-Control"] = IsHashedFilename(path)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        ApplyDefaultHeaders(e);
        return true;
    }

    private bool HandleViaEmbeddedResource(WebResourceRequestedEventArgs e, string path)
    {
        var assembly = _options.ResourceAssembly!;
        var prefix = _options.EmbeddedResourcePrefix!.Replace('/', '.').Replace('\\', '.');

        // Convert URL path to resource name: "assets/app.js" → "prefix.assets.app.js"
        var resourcePath = path.Replace('/', '.');
        var fullResourceName = $"{assembly.GetName().Name}.{prefix}.{resourcePath}";

        var stream = assembly.GetManifestResourceStream(fullResourceName);
        if (stream is null)
        {
            // Try SPA fallback if the exact file wasn't found (e.g., deep link).
            if (path != _options.FallbackDocument)
            {
                var fallbackName = $"{assembly.GetName().Name}.{prefix}.{_options.FallbackDocument}";
                stream = assembly.GetManifestResourceStream(fallbackName);
                _logger.LogDebug("SPA: resource not found '{Resource}', fallback to '{Fallback}'",
                    fullResourceName, fallbackName);
            }

            if (stream is null)
            {
                _logger.LogDebug("SPA: resource not found '{Resource}'", fullResourceName);
                e.ResponseStatusCode = 404;
                e.ResponseBody = new MemoryStream(Encoding.UTF8.GetBytes("Not Found"));
                e.ResponseContentType = "text/plain";
                e.Handled = true;
                return true;
            }
        }

        var ext = Path.GetExtension(path);
        var contentType = GetMimeType(ext);

        e.ResponseBody = stream;
        e.ResponseContentType = contentType;
        e.ResponseStatusCode = 200;
        e.Handled = true;

        // Caching: hashed filenames (containing hash-like segments) get immutable cache.
        if (e.ResponseHeaders is null)
            e.ResponseHeaders = new Dictionary<string, string>();

        if (IsHashedFilename(path))
            e.ResponseHeaders["Cache-Control"] = "public, max-age=31536000, immutable";
        else
            e.ResponseHeaders["Cache-Control"] = "no-cache";

        ApplyDefaultHeaders(e);

        _logger.LogDebug("SPA: served embedded '{Path}' ({ContentType})", path, contentType);
        return true;
    }

    // ==================== Dev proxy serving ====================

    private bool HandleViaDevProxy(WebResourceRequestedEventArgs e, string path)
    {
        try
        {
            // Synchronous proxy call (WebResourceRequested is synchronous).
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            using var response = _devProxy!.Send(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                // For non-success, try fallback (SPA router).
                if (path != _options.FallbackDocument)
                {
                    using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, _options.FallbackDocument);
                    using var fallbackResponse = _devProxy.Send(fallbackRequest, HttpCompletionOption.ResponseHeadersRead);

                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        return CopyProxyResponse(e, fallbackResponse);
                    }
                }

                e.ResponseStatusCode = (int)response.StatusCode;
                e.ResponseBody = new MemoryStream(Encoding.UTF8.GetBytes($"Dev server returned {response.StatusCode}"));
                e.ResponseContentType = "text/plain";
                e.Handled = true;
                return true;
            }

            return CopyProxyResponse(e, response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SPA: dev proxy failed for '{Path}'", path);
            e.ResponseStatusCode = 502;
            e.ResponseBody = new MemoryStream(Encoding.UTF8.GetBytes($"Dev proxy error: {ex.Message}"));
            e.ResponseContentType = "text/plain";
            e.Handled = true;
            return true;
        }
    }

    private bool CopyProxyResponse(WebResourceRequestedEventArgs e, HttpResponseMessage response)
    {
        // Copy the response body to a MemoryStream (required because the HttpResponseMessage will be disposed).
        var ms = new MemoryStream();
        response.Content.CopyTo(ms, null, CancellationToken.None);
        ms.Position = 0;

        e.ResponseBody = ms;
        e.ResponseContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        e.ResponseStatusCode = (int)response.StatusCode;
        e.Handled = true;

        ApplyDefaultHeaders(e);

        _logger.LogDebug("SPA: proxied '{Path}' → {Status} ({ContentType})",
            response.RequestMessage?.RequestUri?.PathAndQuery, e.ResponseStatusCode, e.ResponseContentType);
        return true;
    }

    // ==================== Service worker serving ====================

    private static bool ServeServiceWorkerScript(WebResourceRequestedEventArgs e, ServiceWorkerOptions sw)
    {
        var script = ServiceWorkerRegistrar.GenerateServiceWorkerScript(sw);
        e.ResponseBody = new MemoryStream(Encoding.UTF8.GetBytes(script));
        e.ResponseContentType = "application/javascript";
        e.ResponseStatusCode = 200;
        e.Handled = true;
        e.ResponseHeaders ??= new Dictionary<string, string>();
        e.ResponseHeaders["Cache-Control"] = "no-cache";
        return true;
    }

    private static Stream InjectSwRegistrationScript(Stream htmlBody, ServiceWorkerOptions sw)
    {
        var registrationScript = ServiceWorkerRegistrar.GenerateRegistrationScript(sw);
        var tag = $"<script>{registrationScript}</script>";

        string html;
        using (var reader = new StreamReader(htmlBody, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false))
            html = reader.ReadToEnd();

        var insertAt = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (insertAt < 0)
            insertAt = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);

        var injected = insertAt >= 0
            ? string.Concat(html.AsSpan(0, insertAt), tag, html.AsSpan(insertAt))
            : html + tag;

        return new MemoryStream(Encoding.UTF8.GetBytes(injected));
    }

    // ==================== Helpers ====================

    internal static string GetMimeType(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return "application/octet-stream";
        return MimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";
    }

    /// <summary>
    /// Detects hashed filenames typical in bundler output: app.a1b2c3d4.js, chunk-ABC123.css.
    /// </summary>
    internal static bool IsHashedFilename(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name is null) return false;

        // Pattern: contains a segment of 8+ hex chars (Vite/webpack chunk hash).
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
        {
            var suffix = name[(lastDot + 1)..];
            if (suffix.Length >= 8 && suffix.All(c => char.IsAsciiHexDigit(c)))
                return true;
        }

        // Pattern: contains a dash followed by 6+ hex chars.
        var lastDash = name.LastIndexOf('-');
        if (lastDash >= 0)
        {
            var suffix = name[(lastDash + 1)..];
            if (suffix.Length >= 6 && suffix.All(c => char.IsAsciiHexDigit(c)))
                return true;
        }

        return false;
    }

    private void ApplyDefaultHeaders(WebResourceRequestedEventArgs e)
    {
        if (_options.DefaultHeaders is null) return;
        e.ResponseHeaders ??= new Dictionary<string, string>();
        foreach (var kv in _options.DefaultHeaders)
            e.ResponseHeaders[kv.Key] = kv.Value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _devProxy?.Dispose();
    }
}
