namespace Agibuild.Fulora;

/// <summary>
/// Marker interface for typed event channels on <see cref="JsExportAttribute"/> interfaces.
/// Properties of this type on a [JsExport] interface are automatically wired as push event channels
/// from C# to JavaScript by the source generator.
/// </summary>
/// <typeparam name="T">The event payload type.</typeparam>
public interface IBridgeEvent<T> { }

/// <summary>
/// Runtime implementation of <see cref="IBridgeEvent{T}"/> that allows C# code to push events to JavaScript.
/// <para>
/// Declare properties as <see cref="IBridgeEvent{T}"/> on the interface, and use <see cref="BridgeEvent{T}"/>
/// in the implementation to emit events:
/// <code>
/// private readonly BridgeEvent&lt;Notification&gt; _onNew = new();
/// public IBridgeEvent&lt;Notification&gt; OnNew => _onNew;
/// // Push: _onNew.Emit(new Notification(...));
/// </code>
/// </para>
/// </summary>
/// <typeparam name="T">The event payload type.</typeparam>
public sealed class BridgeEvent<T> : IBridgeEvent<T>
{
    private Action<T>? _emitHandler;
    private readonly object _lock = new();

    /// <summary>
    /// Pushes an event payload to all active JavaScript subscribers.
    /// No-op if no subscribers are connected or the service has not been exposed.
    /// </summary>
    public void Emit(T payload)
    {
        Action<T>? handler;
        lock (_lock) { handler = _emitHandler; }
        handler?.Invoke(payload);
    }

    /// <summary>Connects the RPC push delegate. Called by generated registration code.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void Connect(Action<T> handler)
    {
        lock (_lock) { _emitHandler = handler; }
    }

    /// <summary>Disconnects the RPC push delegate. Called on Remove.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void Disconnect()
    {
        lock (_lock) { _emitHandler = null; }
    }
}

/// <summary>
/// Type-safe bridge service for exposing C# services to JavaScript and importing JS services into C#.
/// <para>
/// Use <see cref="Expose{T}"/> with <see cref="JsExportAttribute"/> interfaces to register
/// C# implementations callable from JS.
/// </para>
/// <para>
/// Use <see cref="GetProxy{T}"/> with <see cref="JsImportAttribute"/> interfaces to obtain
/// a typed C# proxy that calls JS methods.
/// </para>
/// </summary>
public interface IBridgeService
{
    /// <summary>
    /// Registers a C# implementation of a <see cref="JsExportAttribute"/>-marked interface,
    /// making its methods callable from JavaScript via the bridge.
    /// </summary>
    /// <typeparam name="T">An interface decorated with <see cref="JsExportAttribute"/>.</typeparam>
    /// <param name="implementation">The C# object implementing <typeparamref name="T"/>.</param>
    /// <param name="options">Optional per-service bridge options (origin allowlist, etc.).</param>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not decorated with <see cref="JsExportAttribute"/>,
    /// or the service has already been exposed without being removed first.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bridge has been disposed.</exception>
    void Expose<T>(T implementation, BridgeOptions? options = null) where T : class;

    /// <summary>
    /// Returns a typed proxy for a <see cref="JsImportAttribute"/>-marked interface.
    /// Each method call on the proxy is forwarded to the corresponding JS implementation via RPC.
    /// </summary>
    /// <typeparam name="T">An interface decorated with <see cref="JsImportAttribute"/>.</typeparam>
    /// <returns>A proxy implementing <typeparamref name="T"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not decorated with <see cref="JsImportAttribute"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bridge has been disposed.</exception>
    T GetProxy<T>() where T : class;

    /// <summary>
    /// Removes a previously exposed <see cref="JsExportAttribute"/> service,
    /// unregistering all its RPC handlers.
    /// </summary>
    /// <typeparam name="T">The interface type previously passed to <see cref="Expose{T}"/>.</typeparam>
    /// <exception cref="ObjectDisposedException">The bridge has been disposed.</exception>
    void Remove<T>() where T : class;
}

/// <summary>
/// Per-service options for <see cref="IBridgeService.Expose{T}"/>.
/// </summary>
public sealed class BridgeOptions
{
    /// <summary>
    /// Origin allowlist for this service. When <c>null</c>, inherits from
    /// the global <c>WebMessageBridgeOptions.AllowedOrigins</c>.
    /// </summary>
    public IReadOnlySet<string>? AllowedOrigins { get; init; }

    /// <summary>
    /// Rate limit for this service. When <c>null</c>, no rate limiting is applied.
    /// </summary>
    public RateLimit? RateLimit { get; init; }

    /// <summary>
    /// Middleware pipeline applied to every RPC handler for this service, in order.
    /// </summary>
    public IReadOnlyList<IBridgeMiddleware>? Middleware { get; init; }
}

/// <summary>
/// Delegate representing the next step in the middleware pipeline (or the terminal handler).
/// </summary>
public delegate Task<object?> BridgeCallHandler(BridgeCallContext context);

/// <summary>
/// ASP.NET Core–style middleware for intercepting bridge RPC calls (logging, auth, error transform, etc.).
/// </summary>
public interface IBridgeMiddleware
{
    /// <summary>
    /// Processes a bridge call. Call <paramref name="pipeline"/> to continue the pipeline,
    /// or short-circuit by returning without calling it.
    /// </summary>
    Task<object?> InvokeAsync(BridgeCallContext context, BridgeCallHandler pipeline);
}

/// <summary>
/// Context object passed through the bridge middleware pipeline.
/// </summary>
public sealed class BridgeCallContext
{
    /// <summary>The RPC service name (e.g. "AppService").</summary>
    public required string ServiceName { get; init; }

    /// <summary>The RPC method name (e.g. "getCurrentUser").</summary>
    public required string MethodName { get; init; }

    /// <summary>The deserialized JSON arguments, or <c>null</c> when no arguments were sent.</summary>
    public System.Text.Json.JsonElement? Arguments { get; init; }

    /// <summary>Cancellation token for the call (cancelled on <c>$/cancelRequest</c>).</summary>
    public System.Threading.CancellationToken CancellationToken { get; init; }

    /// <summary>Arbitrary properties bag for middleware to share data along the pipeline.</summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
}

/// <summary>
/// Defines a sliding-window rate limit: at most <see cref="MaxCalls"/> calls per <see cref="Window"/>.
/// </summary>
public sealed class RateLimit
{
    /// <summary>Maximum number of calls allowed within <see cref="Window"/>.</summary>
    public int MaxCalls { get; }

    /// <summary>Time window for the sliding-window rate limit.</summary>
    public TimeSpan Window { get; }

    /// <summary>Creates a new sliding-window rate limit.</summary>
    /// <param name="maxCalls">Maximum number of calls allowed within <paramref name="window"/>.</param>
    /// <param name="window">Time window for the rate limit.</param>
    public RateLimit(int maxCalls, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxCalls, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        MaxCalls = maxCalls;
        Window = window;
    }
}
