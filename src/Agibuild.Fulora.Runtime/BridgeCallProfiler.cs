using System.Collections.Concurrent;

namespace Agibuild.Fulora;

/// <summary>
/// Aggregated statistics for a single bridge method.
/// </summary>
/// <param name="ServiceName">The service name.</param>
/// <param name="MethodName">The method name.</param>
/// <param name="CallCount">Total number of completed calls (success + error).</param>
/// <param name="ErrorCount">Number of calls that ended in error.</param>
/// <param name="ErrorRate">Error count divided by call count (0–1).</param>
/// <param name="MinLatencyMs">Minimum observed latency in milliseconds.</param>
/// <param name="MaxLatencyMs">Maximum observed latency in milliseconds.</param>
/// <param name="AvgLatencyMs">Average latency in milliseconds.</param>
/// <param name="P50LatencyMs">50th percentile (median) latency in milliseconds.</param>
/// <param name="P95LatencyMs">95th percentile latency in milliseconds.</param>
/// <param name="P99LatencyMs">99th percentile latency in milliseconds.</param>
public sealed record MethodProfileStats(
    string ServiceName,
    string MethodName,
    long CallCount,
    long ErrorCount,
    double ErrorRate,
    long MinLatencyMs,
    long MaxLatencyMs,
    double AvgLatencyMs,
    long P50LatencyMs,
    long P95LatencyMs,
    long P99LatencyMs);

/// <summary>
/// Aggregated statistics for a bridge service (all methods).
/// </summary>
/// <param name="ServiceName">The service name.</param>
/// <param name="TotalCalls">Total number of calls across all methods.</param>
/// <param name="TotalErrors">Total number of errors across all methods.</param>
/// <param name="ErrorRate">Total errors divided by total calls (0–1).</param>
/// <param name="AvgLatencyMs">Weighted average latency.</param>
/// <param name="Methods">Per-method statistics.</param>
public sealed record ServiceProfileStats(
    string ServiceName,
    long TotalCalls,
    long TotalErrors,
    double ErrorRate,
    double AvgLatencyMs,
    IReadOnlyList<MethodProfileStats> Methods);

/// <summary>
/// An <see cref="IBridgeTracer"/> that collects per-method and per-service statistics
/// for call counts, errors, and latency percentiles. Optionally delegates to an inner tracer.
/// </summary>
public sealed class BridgeCallProfiler : IBridgeTracer
{
    private const int MaxLatencySamples = 10000;

    private readonly IBridgeTracer? _inner;
    private readonly IFuloraDiagnosticsSink? _diagnosticsSink;
    private readonly ConcurrentDictionary<string, MethodCallStats> _stats = new();

    /// <summary>
    /// Creates a profiler with optional inner tracer delegation.
    /// </summary>
    /// <param name="inner">Optional inner tracer to delegate to.</param>
    /// <param name="diagnosticsSink">Optional unified diagnostics sink for normalized bridge events.</param>
    public BridgeCallProfiler(IBridgeTracer? inner = null, IFuloraDiagnosticsSink? diagnosticsSink = null)
    {
        _inner = inner is NullBridgeTracer ? null : inner;
        _diagnosticsSink = diagnosticsSink;
    }

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent("bridge.export.start", serviceName, methodName, "started"));
        _inner?.OnExportCallStart(serviceName, methodName, paramsJson);
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        RecordLatency(serviceName, methodName, elapsedMs, isError: false);
        _diagnosticsSink?.OnEvent(CreateBridgeEvent("bridge.export.end", serviceName, methodName, "success", elapsedMs));
        _inner?.OnExportCallEnd(serviceName, methodName, elapsedMs, resultType);
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
        RecordLatency(serviceName, methodName, elapsedMs, isError: true);
        _diagnosticsSink?.OnEvent(CreateBridgeEvent("bridge.export.error", serviceName, methodName, "error", elapsedMs, exception.GetType().Name));
        _inner?.OnExportCallError(serviceName, methodName, elapsedMs, exception);
    }

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent("bridge.import.start", serviceName, methodName, "started"));
        _inner?.OnImportCallStart(serviceName, methodName, paramsJson);
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        RecordLatency(serviceName, methodName, elapsedMs, isError: false);
        _diagnosticsSink?.OnEvent(CreateBridgeEvent("bridge.import.end", serviceName, methodName, "success", elapsedMs));
        _inner?.OnImportCallEnd(serviceName, methodName, elapsedMs);
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent("bridge.service.exposed", serviceName, null, "exposed"));
        _inner?.OnServiceExposed(serviceName, methodCount, isSourceGenerated);
    }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent("bridge.service.removed", serviceName, null, "removed"));
        _inner?.OnServiceRemoved(serviceName);
    }

    /// <summary>
    /// Gets statistics for a specific method, or null if no calls have been recorded.
    /// </summary>
    public MethodProfileStats? GetMethodStats(string serviceName, string methodName)
    {
        var key = MakeKey(serviceName, methodName);
        return _stats.TryGetValue(key, out var s) ? s.ToMethodProfileStats(serviceName, methodName) : null;
    }

    /// <summary>
    /// Gets aggregated statistics for a service (all methods), or null if no calls have been recorded.
    /// </summary>
    public ServiceProfileStats? GetServiceStats(string serviceName)
    {
        var methods = _stats
            .Where(kv => kv.Key.StartsWith(serviceName + ".", StringComparison.Ordinal))
            .Select(kv => kv.Value.ToMethodProfileStats(serviceName, kv.Key[(serviceName.Length + 1)..]))
            .Where(m => m != null)
            .Cast<MethodProfileStats>()
            .ToList();

        if (methods.Count == 0)
            return null;

        var totalCalls = methods.Sum(m => m.CallCount);
        var totalErrors = methods.Sum(m => m.ErrorCount);
        var errorRate = totalCalls > 0 ? (double)totalErrors / totalCalls : 0.0;
        var avgLatency = totalCalls > 0
            ? methods.Sum(m => m.AvgLatencyMs * m.CallCount) / totalCalls
            : 0;

        return new ServiceProfileStats(
            ServiceName: serviceName,
            TotalCalls: totalCalls,
            TotalErrors: totalErrors,
            ErrorRate: errorRate,
            AvgLatencyMs: avgLatency,
            Methods: methods);
    }

    /// <summary>
    /// Returns the top N methods by average latency (slowest first).
    /// </summary>
    public IReadOnlyList<MethodProfileStats> GetSlowestCalls(int count)
    {
        return _stats
            .Select(kv =>
            {
                var parts = kv.Key.Split('.', 2);
                return kv.Value.ToMethodProfileStats(parts[0], parts.Length > 1 ? parts[1] : "");
            })
            .Where(m => m != null && m.CallCount > 0)
            .OrderByDescending(m => m!.AvgLatencyMs)
            .Take(count)
            .Cast<MethodProfileStats>()
            .ToList();
    }

    /// <summary>
    /// Returns aggregated stats for all services that have recorded calls.
    /// </summary>
    public IReadOnlyList<ServiceProfileStats> GetAllServiceStats()
    {
        var serviceNames = _stats.Keys
            .Select(k => k.Split('.', 2)[0])
            .Distinct()
            .ToList();

        return serviceNames
            .Select(GetServiceStats)
            .Where(s => s != null)
            .Cast<ServiceProfileStats>()
            .ToList();
    }

    /// <summary>
    /// Exports all statistics as JSON.
    /// </summary>
    public string ExportToJson()
    {
        var list = GetAllServiceStats();
        return System.Text.Json.JsonSerializer.Serialize(list, BridgeProfilerJsonContext.Default.ListServiceProfileStats);
    }

    /// <summary>
    /// Clears all collected statistics.
    /// </summary>
    public void Reset()
    {
        _stats.Clear();
    }

    private void RecordLatency(string serviceName, string methodName, long elapsedMs, bool isError)
    {
        var key = MakeKey(serviceName, methodName);
        var stats = _stats.GetOrAdd(key, _ => new MethodCallStats(MaxLatencySamples));
        stats.Record(elapsedMs, isError);
    }

    private static FuloraDiagnosticsEvent CreateBridgeEvent(
        string eventName,
        string serviceName,
        string? methodName,
        string status,
        long? durationMs = null,
        string? errorType = null)
        => new()
        {
            EventName = eventName,
            Layer = "bridge",
            Component = nameof(BridgeCallProfiler),
            Service = serviceName,
            Method = methodName,
            DurationMs = durationMs,
            Status = status,
            ErrorType = errorType
        };

    private static string MakeKey(string serviceName, string methodName) => $"{serviceName}.{methodName}";

    private sealed class MethodCallStats
    {
        private readonly int _maxSamples;
        private readonly object _lock = new();
        private readonly List<long> _latencies = [];
        private long _callCount;
        private long _errorCount;

        public MethodCallStats(int maxSamples)
        {
            _maxSamples = maxSamples;
        }

        public void Record(long elapsedMs, bool isError)
        {
            lock (_lock)
            {
                _callCount++;
                if (isError) _errorCount++;
                _latencies.Add(elapsedMs);
                if (_latencies.Count > _maxSamples)
                    _latencies.RemoveAt(0);
            }
        }

        public MethodProfileStats? ToMethodProfileStats(string serviceName, string methodName)
        {
            lock (_lock)
            {
                if (_callCount == 0)
                    return null;

                var errorRate = (double)_errorCount / _callCount;
                long min = 0, max = 0;
                double avg = 0;
                long p50 = 0, p95 = 0, p99 = 0;

                if (_latencies.Count > 0)
                {
                    var sorted = _latencies.ToList();
                    sorted.Sort();
                    min = sorted[0];
                    max = sorted[^1];
                    avg = sorted.Average();
                    var n = sorted.Count;
                    p50 = Percentile(sorted, n, 0.50);
                    p95 = Percentile(sorted, n, 0.95);
                    p99 = Percentile(sorted, n, 0.99);
                }

                return new MethodProfileStats(
                    ServiceName: serviceName,
                    MethodName: methodName,
                    CallCount: _callCount,
                    ErrorCount: _errorCount,
                    ErrorRate: errorRate,
                    MinLatencyMs: min,
                    MaxLatencyMs: max,
                    AvgLatencyMs: avg,
                    P50LatencyMs: p50,
                    P95LatencyMs: p95,
                    P99LatencyMs: p99);
            }
        }

        private static long Percentile(List<long> sorted, int n, double p)
        {
            if (n == 0) return 0;
            var idx = (int)Math.Ceiling(p * n) - 1;
            idx = Math.Max(0, idx);
            return sorted[idx];
        }
    }
}
