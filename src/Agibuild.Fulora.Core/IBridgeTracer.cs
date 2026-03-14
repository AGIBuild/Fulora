namespace Agibuild.Fulora;

/// <summary>
/// Optional tracer for Bridge method calls. Implement this interface to observe
/// all RPC traffic for debugging, performance monitoring, or visualization.
/// </summary>
public interface IBridgeTracer
{
    /// <summary>Called when a JS→C# method invocation starts.</summary>
    void OnExportCallStart(string serviceName, string methodName, string? paramsJson);

    /// <summary>Called when a JS→C# method invocation completes successfully.</summary>
    void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType);

    /// <summary>Called when a JS→C# method invocation fails.</summary>
    void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception);

    /// <summary>Called when a C#→JS method invocation is sent.</summary>
    void OnImportCallStart(string serviceName, string methodName, string? paramsJson);

    /// <summary>Called when a C#→JS method invocation completes.</summary>
    void OnImportCallEnd(string serviceName, string methodName, long elapsedMs);

    /// <summary>Called when a service is exposed.</summary>
    void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated);

    /// <summary>Called when a service is removed.</summary>
    void OnServiceRemoved(string serviceName);
}

/// <summary>
/// A no-op tracer that discards all events. Used when no tracing is configured.
/// </summary>
public sealed class NullBridgeTracer : IBridgeTracer
{
    /// <summary>Singleton instance.</summary>
    public static readonly NullBridgeTracer Instance = new();

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson) { }
    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType) { }
    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception) { }
    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson) { }
    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs) { }
    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated) { }
    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName) { }
}
