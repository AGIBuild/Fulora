using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Default tracer that logs all bridge calls via <see cref="ILogger"/>.
/// Attach to <see cref="RuntimeBridgeService"/> for structured call tracing.
/// </summary>
public sealed class LoggingBridgeTracer : IBridgeTracer
{
    private readonly ILogger _logger;

    /// <inheritdoc />
    public LoggingBridgeTracer(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
        => _logger.LogTrace("Bridge → {Service}.{Method} params={Params}",
            serviceName, methodName, Truncate(paramsJson));

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
        => _logger.LogTrace("Bridge ← {Service}.{Method} {Elapsed}ms result={ResultType}",
            serviceName, methodName, elapsedMs, resultType ?? "void");

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
        => _logger.LogWarning(exception, "Bridge ✗ {Service}.{Method} {Elapsed}ms",
            serviceName, methodName, elapsedMs);

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
        => _logger.LogTrace("Bridge ⇒ JS {Service}.{Method} params={Params}",
            serviceName, methodName, Truncate(paramsJson));

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
        => _logger.LogTrace("Bridge ⇐ JS {Service}.{Method} {Elapsed}ms",
            serviceName, methodName, elapsedMs);

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
        => _logger.LogInformation("Bridge: exposed {Service} ({Count} methods, {Mode})",
            serviceName, methodCount, isSourceGenerated ? "source-generated" : "reflection");

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
        => _logger.LogInformation("Bridge: removed {Service}", serviceName);

    private static string? Truncate(string? s, int maxLen = 200)
        => s is null ? null : s.Length <= maxLen ? s : s[..maxLen] + "…";
}
