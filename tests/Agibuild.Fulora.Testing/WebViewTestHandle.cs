using System.Text.Json;
using Agibuild.Fulora;

namespace Agibuild.Fulora.Testing;

/// <summary>
/// Test handle for interacting with a WebView in hybrid E2E tests.
/// Provides JS execution, DOM waiting, and bridge call observation.
/// </summary>
public sealed class WebViewTestHandle
{
    private readonly WebViewCore _core;
    private readonly BridgeTestTracer _tracer;
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Creates a new handle for the given core and tracer.
    /// </summary>
    public WebViewTestHandle(WebViewCore core, BridgeTestTracer tracer)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    /// <summary>
    /// Executes JavaScript in the WebView and returns the result as a string.
    /// </summary>
    public Task<string?> EvaluateJsAsync(string script, CancellationToken ct = default)
        => _core.InvokeScriptAsync(script);

    /// <summary>
    /// Waits until the bridge is ready on the JS side (window.__agibuild?.bridge?.ready).
    /// </summary>
    public async Task WaitForBridgeReadyAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? DefaultTimeout);
        var script = "(function(){ return !!(window.__agibuild && window.__agibuild.bridge && window.__agibuild.bridge.ready); })()";

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _core.InvokeScriptAsync(script).ConfigureAwait(false);
            if (string.Equals(result?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            await Task.Delay(DefaultPollInterval, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("Bridge was not ready within the timeout.");
    }

    /// <summary>
    /// Waits until an element matching the selector exists in the DOM.
    /// </summary>
    public async Task WaitForElementAsync(string selector, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? DefaultTimeout);
        var escapedSelector = JsonSerializer.Serialize(selector);
        var script = $"(function(){{ return !!document.querySelector({escapedSelector}); }})()";

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _core.InvokeScriptAsync(script).ConfigureAwait(false);
            if (string.Equals(result?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            await Task.Delay(DefaultPollInterval, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"Element '{selector}' was not found within the timeout.");
    }

    /// <summary>
    /// Clicks the element matching the selector.
    /// </summary>
    public async Task ClickElementAsync(string selector, CancellationToken ct = default)
    {
        var escapedSelector = JsonSerializer.Serialize(selector);
        var script = $"(function(){{ var el = document.querySelector({escapedSelector}); if(el) el.click(); }})()";
        await _core.InvokeScriptAsync(script).ConfigureAwait(false);
    }

    /// <summary>
    /// Types text into the element matching the selector (sets value and fires input/change events).
    /// </summary>
    public async Task TypeTextAsync(string selector, string text, CancellationToken ct = default)
    {
        var escapedSelector = JsonSerializer.Serialize(selector);
        var escapedText = JsonSerializer.Serialize(text);
        var script = "(function(){ var el = document.querySelector(" + escapedSelector + "); if (el) { el.value = " + escapedText + "; el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); } })()";
        await _core.InvokeScriptAsync(script).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns recorded bridge calls, optionally filtered by service name.
    /// </summary>
    public IReadOnlyList<BridgeCallRecord> GetBridgeCalls(string? serviceFilter = null)
        => _tracer.GetBridgeCalls(serviceFilter);

    /// <summary>
    /// Waits for a bridge call matching the given service and method.
    /// </summary>
    public Task<BridgeCallRecord> WaitForBridgeCallAsync(
        string service,
        string method,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => _tracer.WaitForBridgeCallAsync(service, method, timeout, ct);
}
