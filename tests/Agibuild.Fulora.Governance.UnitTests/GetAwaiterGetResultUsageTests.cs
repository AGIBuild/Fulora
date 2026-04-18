using System.Text;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class GetAwaiterGetResultUsageTests
{
    private sealed record AllowedBlockingCall(string Fragment, string Owner, string Rationale);

    [Fact]
    public void Production_sources_use_GetAwaiterGetResult_only_in_whitelisted_sync_boundaries()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        var files = Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories);

        var allowed = new Dictionary<string, AllowedBlockingCall[]>
        {
            ["src/Agibuild.Fulora.Runtime/WebViewCore.cs"] =
            [
                new(
                    "TryGetWebViewHandleAsync().GetAwaiter().GetResult()",
                    "WebViewCore",
                    "Synchronous compatibility wrapper for native handle retrieval while async-first migration is in progress.")
            ],
            ["src/Agibuild.Fulora.Platforms/Windows/WindowsWebViewAdapter.cs"] =
            [
                new(
                    "decisionTask.AsTask().GetAwaiter().GetResult()",
                    "WindowsWebViewAdapter",
                    "WebView2 native navigation callback requires a synchronous allow/deny decision."),
                new(
                    "AddPreloadScriptAsync(javaScript).GetAwaiter().GetResult()",
                    "WindowsWebViewAdapter",
                    "Legacy sync preload API bridges to async implementation for compatibility.")
            ],
            ["src/Agibuild.Fulora.Adapters.Android/AndroidWebViewAdapter.cs"] =
            [
                new(
                    "decisionTask.AsTask().GetAwaiter().GetResult()",
                    "AndroidWebViewAdapter",
                    "Android should-override-url-loading callback requires a synchronous navigation decision."),
                new(
                    "tcs.Task.GetAwaiter().GetResult()",
                    "AndroidWebViewAdapter",
                    "RunOnUiThread helper blocks only at adapter boundary to satisfy platform callback shape.")
            ],
            ["src/Agibuild.Fulora.Telemetry.Sentry/SentryTelemetryProvider.cs"] =
            [
                new(
                    "FlushAsync(_options.FlushTimeout).GetAwaiter().GetResult()",
                    "SentryTelemetryProvider",
                    "ITelemetryProvider.Flush() is synchronous by contract; Sentry SDK only provides async flush.")
            ],
            ["src/Agibuild.Fulora.Runtime/RuntimeBridgeService.cs"] =
            [
                new(
                    "asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult()",
                    "RuntimeBridgeService",
                    "IDisposable.Dispose() is synchronous; IAsyncDisposable implementations must be awaited at the sync bridge boundary.")
            ]
        };

        var found = new List<(string File, string Line)>();
        foreach (var file in files)
        {
            foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (!line.Contains(".GetAwaiter().GetResult()", StringComparison.Ordinal))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                found.Add((relative, line.Trim()));
            }
        }

        foreach (var (file, line) in found)
        {
            if (!allowed.TryGetValue(file, out var entries))
            {
                Assert.Fail($"Unexpected GetAwaiter().GetResult() in {file}: {line}");
            }

            Assert.Contains(entries, entry => line.Contains(entry.Fragment, StringComparison.Ordinal));
        }

        var expectedCount = allowed.Values.Sum(x => x.Length);
        Assert.Equal(expectedCount, found.Count);

        foreach (var (file, entries) in allowed)
        {
            foreach (var entry in entries)
            {
                Assert.Contains(found, x => x.File == file && x.Line.Contains(entry.Fragment, StringComparison.Ordinal));
                Assert.False(string.IsNullOrWhiteSpace(entry.Owner));
                Assert.False(string.IsNullOrWhiteSpace(entry.Rationale));
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Agibuild.Fulora.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
