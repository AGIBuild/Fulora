using System.Reflection;
using System.Text.Json;

namespace Agibuild.Fulora;

/// <summary>
/// Provides the Bridge DevTools overlay for debugging bridge calls.
/// Serves the overlay HTML and pushes events to the WebView in real-time.
/// </summary>
public sealed class BridgeDevToolsService : IDisposable
{
    private readonly BridgeEventCollector _collector;
    private readonly DevToolsPanelTracer _tracer;
    private IDisposable? _subscription;
    private Func<string, Task<string?>>? _invokeScript;
    private volatile bool _disposed;

    private static readonly JsonSerializerOptions EventJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Initializes the DevTools service with an optional existing tracer and buffer capacity.</summary>
    public BridgeDevToolsService(IBridgeTracer? existingTracer = null, int bufferCapacity = 500)
    {
        _collector = new BridgeEventCollector(bufferCapacity);
        _tracer = new DevToolsPanelTracer(_collector, existingTracer);
    }

    /// <summary>
    /// The tracer to wire into <see cref="RuntimeBridgeService"/>.
    /// Pass this as the tracer parameter when constructing the bridge service.
    /// </summary>
    public IBridgeTracer Tracer => _tracer;

    /// <summary>The underlying event collector for testing or direct access.</summary>
    public IBridgeEventCollector Collector => _collector;

    /// <summary>
    /// Returns the self-contained DevTools overlay HTML from embedded resources.
    /// </summary>
    public static string GetOverlayHtml()
    {
        var assembly = typeof(BridgeDevToolsService).Assembly;
        using var stream = assembly.GetManifestResourceStream("Agibuild.Fulora.DevToolsPanel.html")
            ?? throw new InvalidOperationException("DevTools overlay HTML resource not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Starts pushing events to the WebView overlay in real-time.
    /// Call this after the overlay WebView has loaded the DevTools HTML.
    /// </summary>
    /// <param name="invokeScript">
    /// Function to invoke JavaScript in the overlay WebView.
    /// Typically <c>webView.InvokeScriptAsync</c>.
    /// </param>
    public void StartPushing(Func<string, Task<string?>> invokeScript)
    {
        ArgumentNullException.ThrowIfNull(invokeScript);
        _invokeScript = invokeScript;

        var existing = _collector.GetEvents();
        if (existing.Count > 0)
        {
            var json = JsonSerializer.Serialize(existing, EventJsonOptions);
            _ = invokeScript($"window.__bridgeDevToolsLoadEvents({json})");
        }

        _subscription?.Dispose();
        _subscription = _collector.Subscribe(evt =>
        {
            if (_disposed || _invokeScript is null) return;
            var json = JsonSerializer.Serialize(evt, EventJsonOptions);
            _ = _invokeScript($"window.__bridgeDevToolsAddEvent({json})");
        });
    }

    /// <summary>Stops pushing events and clears the subscription.</summary>
    public void StopPushing()
    {
        _subscription?.Dispose();
        _subscription = null;
        _invokeScript = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPushing();
    }
}
