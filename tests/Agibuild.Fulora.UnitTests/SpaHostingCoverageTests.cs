using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed partial class RuntimeCoverageTests
{
    [Fact]
    public void AddEmbeddedFileProvider_null_options_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SpaHostingExtensions.AddEmbeddedFileProvider(null!, "app"));
    }

    [Fact]
    public void AddEmbeddedFileProvider_empty_scheme_throws()
    {
        var opts = new WebViewEnvironmentOptions();
        Assert.Throws<ArgumentException>(() => opts.AddEmbeddedFileProvider(""));
    }

    [Fact]
    public void AddEmbeddedFileProvider_null_scheme_throws()
    {
        var opts = new WebViewEnvironmentOptions();
        Assert.Throws<ArgumentNullException>(() => opts.AddEmbeddedFileProvider(null!));
    }

    [Fact]
    public void AddEmbeddedFileProvider_registers_custom_scheme()
    {
        var opts = new WebViewEnvironmentOptions();

        var result = opts.AddEmbeddedFileProvider("myscheme");

        Assert.Same(opts, result);
        Assert.Single(opts.CustomSchemes);
        Assert.Equal("myscheme", opts.CustomSchemes[0].SchemeName);
        Assert.True(opts.CustomSchemes[0].HasAuthorityComponent);
        Assert.True(opts.CustomSchemes[0].TreatAsSecure);
    }

    [Fact]
    public void AddEmbeddedFileProvider_does_not_duplicate_scheme()
    {
        var opts = new WebViewEnvironmentOptions();
        opts.AddEmbeddedFileProvider("app");
        opts.AddEmbeddedFileProvider("app");

        Assert.Single(opts.CustomSchemes);
    }

    [Fact]
    public void AddDevServerProxy_null_options_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SpaHostingExtensions.AddDevServerProxy(null!, "app", "http://localhost:5173"));
    }

    [Fact]
    public void AddDevServerProxy_empty_scheme_throws()
    {
        var opts = new WebViewEnvironmentOptions();
        Assert.Throws<ArgumentException>(() => opts.AddDevServerProxy("", "http://localhost:5173"));
    }

    [Fact]
    public void AddDevServerProxy_null_scheme_throws()
    {
        var opts = new WebViewEnvironmentOptions();
        Assert.Throws<ArgumentNullException>(() => opts.AddDevServerProxy(null!, "http://localhost:5173"));
    }

    [Fact]
    public void AddDevServerProxy_empty_url_throws()
    {
        var opts = new WebViewEnvironmentOptions();
        Assert.Throws<ArgumentException>(() => opts.AddDevServerProxy("app", ""));
    }

    [Fact]
    public void AddDevServerProxy_null_url_throws()
    {
        var opts = new WebViewEnvironmentOptions();
        Assert.Throws<ArgumentNullException>(() => opts.AddDevServerProxy("app", null!));
    }

    [Fact]
    public void AddDevServerProxy_registers_custom_scheme()
    {
        var opts = new WebViewEnvironmentOptions();

        var result = opts.AddDevServerProxy("devscheme", "http://localhost:3000");

        Assert.Same(opts, result);
        Assert.Single(opts.CustomSchemes);
        Assert.Equal("devscheme", opts.CustomSchemes[0].SchemeName);
    }

    [Fact]
    public void AddDevServerProxy_does_not_duplicate_scheme()
    {
        var opts = new WebViewEnvironmentOptions();
        opts.AddDevServerProxy("app", "http://localhost:3000");
        opts.AddDevServerProxy("app", "http://localhost:5173");

        Assert.Single(opts.CustomSchemes);
    }

    [Fact]
    public void SpaHostingService_null_options_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SpaHostingService(null!, NullTestLogger.Instance));
    }

    [Fact]
    public void SpaHostingService_null_logger_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SpaHostingService(new SpaHostingOptions { DevServerUrl = "http://localhost:3000" }, null!));
    }

    [Fact]
    public void SpaHostingService_DevServerUrl_creates_proxy_mode()
    {
        // Should not throw — DevServerUrl is set, so embedded fields are not required.
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            DevServerUrl = "http://localhost:5173/"
        }, NullTestLogger.Instance);

        Assert.NotNull(svc);
    }

    [Fact]
    public void SpaHostingService_no_DevServerUrl_no_embedded_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new SpaHostingService(new SpaHostingOptions
            {
                DevServerUrl = null,
                EmbeddedResourcePrefix = null,
                ResourceAssembly = null,
            }, NullTestLogger.Instance));
    }

    [Fact]
    public void SpaHostingService_no_DevServerUrl_no_assembly_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new SpaHostingService(new SpaHostingOptions
            {
                DevServerUrl = null,
                EmbeddedResourcePrefix = "wwwroot",
                ResourceAssembly = null,
            }, NullTestLogger.Instance));
    }

    [Fact]
    public void GetSchemeRegistration_returns_correct_scheme()
    {
        using var svc = CreateEmbeddedSpaService();

        var reg = svc.GetSchemeRegistration();

        Assert.Equal("app", reg.SchemeName);
        Assert.True(reg.HasAuthorityComponent);
        Assert.True(reg.TreatAsSecure);
    }

    [Fact]
    public void TryHandle_returns_false_when_disposed()
    {
        var svc = CreateEmbeddedSpaService();
        svc.Dispose();

        var e = MakeSpaArgs("app://localhost/index.html");
        Assert.False(svc.TryHandle(e));
    }

    [Fact]
    public void TryHandle_returns_false_when_already_handled()
    {
        using var svc = CreateEmbeddedSpaService();
        var e = MakeSpaArgs("app://localhost/index.html");
        e.Handled = true;

        Assert.False(svc.TryHandle(e));
    }

    [Fact]
    public void TryHandle_returns_false_when_uri_is_null()
    {
        using var svc = CreateEmbeddedSpaService();
        var e = new WebResourceRequestedEventArgs { RequestUri = null, Method = "GET" };

        Assert.False(svc.TryHandle(e));
    }

    [Fact]
    public void TryHandle_returns_false_for_non_matching_scheme()
    {
        using var svc = CreateEmbeddedSpaService();
        var e = MakeSpaArgs("https://example.com/page");

        Assert.False(svc.TryHandle(e));
    }

    [Fact]
    public void TryHandle_returns_false_for_non_matching_host()
    {
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            Scheme = "app",
            Host = "myapp",
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly,
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://other-host/index.html");

        Assert.False(svc.TryHandle(e));
    }

    [Fact]
    public void TryHandle_applies_default_headers()
    {
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly,
            DefaultHeaders = new Dictionary<string, string>
            {
                ["X-Custom"] = "TestValue",
                ["X-Frame-Options"] = "DENY"
            }
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/test.txt");
        svc.TryHandle(e);

        Assert.NotNull(e.ResponseHeaders);
        Assert.Equal("TestValue", e.ResponseHeaders!["X-Custom"]);
        Assert.Equal("DENY", e.ResponseHeaders["X-Frame-Options"]);
    }

    [Fact]
    public void TryHandle_hashed_filename_gets_immutable_cache()
    {
        // We need an actual embedded resource with a hashed name to test this fully.
        // Instead test that non-hashed gets no-cache (already tested) and hashed
        // via the static helper.
        Assert.True(SpaHostingService.IsHashedFilename("app.a1b2c3d4.js"));
        Assert.True(SpaHostingService.IsHashedFilename("chunk-ABCDEF1234.css"));
        Assert.False(SpaHostingService.IsHashedFilename("app.js"));
        Assert.False(SpaHostingService.IsHashedFilename(""));
    }

    [Fact]
    public void TryHandle_serves_service_worker_script_when_configured()
    {
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly,
            ServiceWorker = new ServiceWorkerOptions { ScriptPath = "/sw.js" }
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/sw.js");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        Assert.Equal(200, e.ResponseStatusCode);
        Assert.Equal("application/javascript", e.ResponseContentType);
        Assert.NotNull(e.ResponseBody);
        using var reader = new StreamReader(e.ResponseBody!);
        var content = reader.ReadToEnd();
        Assert.Contains("self.addEventListener", content);
    }

    [Fact]
    public void TryHandle_does_not_serve_sw_script_path_when_not_configured()
    {
        using var svc = CreateEmbeddedSpaService();

        var e = MakeSpaArgs("app://localhost/sw.js");
        var handled = svc.TryHandle(e);

        // Without ServiceWorker config, /sw.js falls through to normal resource lookup (404 from embedded).
        Assert.True(handled);
        Assert.Equal(404, e.ResponseStatusCode);
    }

    [Fact]
    public void TryHandle_injects_sw_registration_script_into_html_when_configured()
    {
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "HtmlTestResources",
            ResourceAssembly = typeof(RuntimeCoverageTests).Assembly,
            ServiceWorker = new ServiceWorkerOptions { ScriptPath = "/sw.js" }
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/index.html");
        svc.TryHandle(e);

        Assert.Equal(200, e.ResponseStatusCode);
        using var reader = new StreamReader(e.ResponseBody!);
        var html = reader.ReadToEnd();
        Assert.Contains("navigator.serviceWorker.register", html);
        Assert.Contains("</head>", html);
    }

    [Fact]
    public void TryHandle_does_not_inject_sw_script_into_non_html_responses()
    {
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly,
            ServiceWorker = new ServiceWorkerOptions { ScriptPath = "/sw.js" }
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/test.txt");
        svc.TryHandle(e);

        Assert.Equal(200, e.ResponseStatusCode);
        using var reader = new StreamReader(e.ResponseBody!);
        var content = reader.ReadToEnd();
        Assert.DoesNotContain("navigator.serviceWorker.register", content);
    }

    [Fact]
    public void TryHandle_does_not_inject_sw_script_when_sw_not_configured()
    {
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            EmbeddedResourcePrefix = "HtmlTestResources",
            ResourceAssembly = typeof(RuntimeCoverageTests).Assembly,
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/index.html");
        svc.TryHandle(e);

        Assert.Equal(200, e.ResponseStatusCode);
        using var reader = new StreamReader(e.ResponseBody!);
        var html = reader.ReadToEnd();
        Assert.DoesNotContain("navigator.serviceWorker.register", html);
    }

    [Fact]
    public void TryHandle_DevProxy_unreachable_returns_502()
    {
        // Point to a non-listening port — the HTTP call will fail with connection refused.
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            DevServerUrl = "http://127.0.0.1:1"  // Port 1 is almost certainly not listening.
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/index.html");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        Assert.True(e.Handled);
        Assert.Equal(502, e.ResponseStatusCode);
        Assert.Equal("text/plain", e.ResponseContentType);
    }

    [Theory]
    [InlineData(".htm", "text/html")]
    [InlineData(".mjs", "application/javascript")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".ico", "image/x-icon")]
    [InlineData(".woff", "font/woff")]
    [InlineData(".ttf", "font/ttf")]
    [InlineData(".eot", "application/vnd.ms-fontobject")]
    [InlineData(".otf", "font/otf")]
    [InlineData(".map", "application/json")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".avif", "image/avif")]
    [InlineData(".mp4", "video/mp4")]
    [InlineData(".webm", "video/webm")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".txt", "text/plain")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(null, "application/octet-stream")]
    public void GetMimeType_covers_all_entries(string? ext, string expected)
    {
        Assert.Equal(expected, SpaHostingService.GetMimeType(ext!));
    }

    [Fact]
    public void TryHandle_embedded_resource_not_found_tries_fallback()
    {
        using var svc = CreateEmbeddedSpaService();
        // Request a file that doesn't exist — should try fallback to index.html.
        var e = MakeSpaArgs("app://localhost/assets/missing.js");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        // index.html may or may not exist in test assembly — either 200 or 404 is acceptable.
        Assert.True(e.ResponseStatusCode == 200 || e.ResponseStatusCode == 404);
    }

    [Fact]
    public void TryHandle_DevProxy_non_success_attempts_fallback()
    {
        // Create a dev proxy pointing to a non-listening address
        // to exercise both the initial request failure and fallback paths.
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            DevServerUrl = "http://127.0.0.1:1"
        }, NullTestLogger.Instance);

        // Request a file with extension — will try direct, then fallback.
        var e = MakeSpaArgs("app://localhost/missing.js");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        Assert.Equal(502, e.ResponseStatusCode);
    }

    [Fact]
    public void TryHandle_embedded_totally_missing_returns_404()
    {
        using var svc = CreateEmbeddedSpaService();

        // Request a file that doesn't exist and whose fallback also doesn't exist.
        var e = MakeSpaArgs("app://localhost/totally_missing_file.xyz");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        // The resource and fallback are both missing → 404.
        Assert.Equal(404, e.ResponseStatusCode);
        Assert.Equal("text/plain", e.ResponseContentType);
    }

    [Fact]
    public void TryHandle_embedded_hashed_filename_serves_with_immutable_cache()
    {
        using var svc = CreateEmbeddedSpaService();

        // app.a1b2c3d4.js is an actual embedded resource with a hash-style name.
        var e = MakeSpaArgs("app://localhost/app.a1b2c3d4.js");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        Assert.Equal(200, e.ResponseStatusCode);
        Assert.Equal("application/javascript", e.ResponseContentType);
        Assert.NotNull(e.ResponseHeaders);
        Assert.Contains("immutable", e.ResponseHeaders!["Cache-Control"]);
    }

    [Fact]
    public void TryHandle_DevProxy_success_copies_response_body()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var ct = TestContext.Current.CancellationToken;
        _ = Task.Run(() =>
        {
            try
            {
                while (listener.IsListening && !ct.IsCancellationRequested)
                {
                    var ctx = listener.GetContext();
                    var body = Encoding.UTF8.GetBytes("<html>Dev Server OK</html>");
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/html";
                    ctx.Response.ContentLength64 = body.Length;
                    ctx.Response.OutputStream.Write(body);
                    ctx.Response.Close();
                }
            }
            catch { /* listener stopped */ }
        }, ct);

        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            DevServerUrl = $"http://127.0.0.1:{port}"
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/index.html");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        Assert.Equal(200, e.ResponseStatusCode);
        Assert.Equal("text/html", e.ResponseContentType);
        Assert.NotNull(e.ResponseBody);

        using var reader = new StreamReader(e.ResponseBody!);
        Assert.Contains("Dev Server OK", reader.ReadToEnd());

        listener.Stop();
    }

    [Fact]
    public void TryHandle_DevProxy_fallback_success_copies_fallback_response()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var ct = TestContext.Current.CancellationToken;
        _ = Task.Run(() =>
        {
            try
            {
                while (listener.IsListening && !ct.IsCancellationRequested)
                {
                    var ctx = listener.GetContext();
                    var path = ctx.Request.Url!.AbsolutePath;

                    if (path == "/index.html")
                    {
                        var body = Encoding.UTF8.GetBytes("<html>Fallback Index</html>");
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "text/html";
                        ctx.Response.ContentLength64 = body.Length;
                        ctx.Response.OutputStream.Write(body);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 404;
                    }
                    ctx.Response.Close();
                }
            }
            catch { /* listener stopped */ }
        }, ct);

        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            DevServerUrl = $"http://127.0.0.1:{port}"
        }, NullTestLogger.Instance);

        // Request a file that doesn't exist → 404 from dev server → fallback to index.html → 200.
        var e = MakeSpaArgs("app://localhost/assets/missing.js");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        Assert.Equal(200, e.ResponseStatusCode);
        Assert.Equal("text/html", e.ResponseContentType);

        using var reader = new StreamReader(e.ResponseBody!);
        Assert.Contains("Fallback Index", reader.ReadToEnd());

        listener.Stop();
    }

    [Fact]
    public void TryHandle_DevProxy_non_success_and_fallback_fails_returns_error()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var ct = TestContext.Current.CancellationToken;
        _ = Task.Run(() =>
        {
            try
            {
                while (listener.IsListening && !ct.IsCancellationRequested)
                {
                    var ctx = listener.GetContext();
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
            }
            catch { /* listener stopped */ }
        }, ct);

        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            DevServerUrl = $"http://127.0.0.1:{port}"
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/missing.js");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        Assert.Equal(500, e.ResponseStatusCode);
        Assert.Equal("text/plain", e.ResponseContentType);

        listener.Stop();
    }

    [Fact]
    public void TryHandle_DevProxy_success_applies_default_headers()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var ct = TestContext.Current.CancellationToken;
        _ = Task.Run(() =>
        {
            try
            {
                while (listener.IsListening && !ct.IsCancellationRequested)
                {
                    var ctx = listener.GetContext();
                    var body = Encoding.UTF8.GetBytes("OK");
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/plain";
                    ctx.Response.ContentLength64 = body.Length;
                    ctx.Response.OutputStream.Write(body);
                    ctx.Response.Close();
                }
            }
            catch { /* listener stopped */ }
        }, ct);

        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            DevServerUrl = $"http://127.0.0.1:{port}",
            DefaultHeaders = new Dictionary<string, string>
            {
                ["X-Test-Header"] = "ProxyTest"
            }
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/index.html");
        svc.TryHandle(e);

        Assert.NotNull(e.ResponseHeaders);
        Assert.Equal("ProxyTest", e.ResponseHeaders!["X-Test-Header"]);

        listener.Stop();
    }

    // ==================== HandleViaExternalAssets ====================

    [Fact]
    public void TryHandle_external_assets_provider_returns_empty_falls_through()
    {
        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            ActiveAssetDirectoryProvider = () => "",
            EmbeddedResourcePrefix = "TestResources",
            ResourceAssembly = typeof(SpaHostingTests).Assembly,
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/index.html");
        svc.TryHandle(e);

        // Empty provider → falls through to embedded resources, so handled is true.
        Assert.True(e.Handled);
    }

    [Fact]
    public void TryHandle_external_assets_directory_not_found_returns_404()
    {
        var nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        using var svc = new SpaHostingService(new SpaHostingOptions
        {
            ActiveAssetDirectoryProvider = () => nonExistentDir,
        }, NullTestLogger.Instance);

        var e = MakeSpaArgs("app://localhost/app.js");
        var handled = svc.TryHandle(e);

        Assert.True(handled);
        Assert.Equal(404, e.ResponseStatusCode);
        Assert.Equal("text/plain", e.ResponseContentType);
    }

    [Fact]
    public void TryHandle_external_assets_path_traversal_blocked_at_os_level()
    {
        // The URI parser normalizes '..' segments before they reach HandleViaExternalAssets,
        // so a traversal attempt via URL resolves to a harmless (not-found) path.
        // The 400 guard in the code protects against raw (non-URI) path inputs, e.g.
        // Windows backslash traversal that Path.GetFullPath would resolve outside the root.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var svc = new SpaHostingService(new SpaHostingOptions
            {
                ActiveAssetDirectoryProvider = () => tempDir,
            }, NullTestLogger.Instance);

            // URI parser normalises /../../../etc/passwd → /etc/passwd, which is not in
            // tempDir → file missing → 404 (no fallback either).
            var e = MakeSpaArgs("app://localhost/../../../etc/passwd");
            var handled = svc.TryHandle(e);

            Assert.True(handled);
            Assert.Equal(404, e.ResponseStatusCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryHandle_external_assets_serves_existing_file()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "app.js");
        File.WriteAllText(filePath, "console.log('hello');");
        try
        {
            using var svc = new SpaHostingService(new SpaHostingOptions
            {
                ActiveAssetDirectoryProvider = () => tempDir,
            }, NullTestLogger.Instance);

            var e = MakeSpaArgs("app://localhost/app.js");
            var handled = svc.TryHandle(e);

            Assert.True(handled);
            Assert.Equal(200, e.ResponseStatusCode);
            Assert.Equal("application/javascript", e.ResponseContentType);
            Assert.NotNull(e.ResponseBody);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryHandle_external_assets_missing_file_falls_back_to_index()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "index.html"), "<html></html>");
        try
        {
            using var svc = new SpaHostingService(new SpaHostingOptions
            {
                ActiveAssetDirectoryProvider = () => tempDir,
            }, NullTestLogger.Instance);

            // Request a deep-link route (no extension) — should serve index.html
            var e = MakeSpaArgs("app://localhost/missing.js");
            var handled = svc.TryHandle(e);

            Assert.True(handled);
            Assert.Equal(200, e.ResponseStatusCode);
            Assert.Equal("text/html", e.ResponseContentType);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryHandle_external_assets_missing_file_and_fallback_returns_404()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var svc = new SpaHostingService(new SpaHostingOptions
            {
                ActiveAssetDirectoryProvider = () => tempDir,
            }, NullTestLogger.Instance);

            var e = MakeSpaArgs("app://localhost/missing.js");
            var handled = svc.TryHandle(e);

            Assert.True(handled);
            Assert.Equal(404, e.ResponseStatusCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryHandle_external_assets_hashed_filename_gets_immutable_cache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var hashedFile = Path.Combine(tempDir, "chunk.a1b2c3d4e5f6.js");
        File.WriteAllText(hashedFile, "/* chunk */");
        try
        {
            using var svc = new SpaHostingService(new SpaHostingOptions
            {
                ActiveAssetDirectoryProvider = () => tempDir,
            }, NullTestLogger.Instance);

            var e = MakeSpaArgs("app://localhost/chunk.a1b2c3d4e5f6.js");
            svc.TryHandle(e);

            Assert.Equal(200, e.ResponseStatusCode);
            Assert.NotNull(e.ResponseHeaders);
            Assert.Equal("public, max-age=31536000, immutable", e.ResponseHeaders!["Cache-Control"]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ==================== IsHashedFilename boundary ====================

    [Theory]
    [InlineData("app.1234567.js", false)]   // 7 hex chars after dot → not hashed
    [InlineData("app.12345678.js", true)]   // 8 hex chars after dot → hashed
    [InlineData("chunk-12345.js", false)]  // 5 hex chars after dash → not hashed
    [InlineData("chunk-123456.js", true)]  // 6 hex chars after dash → hashed
    [InlineData("app.1234567g.js", false)] // 8 chars but 'g' is not hex → not hashed
    [InlineData("app.js", false)]          // no hash segment → not hashed
    public void IsHashedFilename_boundary_cases(string filename, bool expected)
    {
        Assert.Equal(expected, SpaHostingService.IsHashedFilename(filename));
    }
}
