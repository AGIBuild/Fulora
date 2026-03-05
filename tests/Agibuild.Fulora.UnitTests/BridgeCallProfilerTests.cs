using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class BridgeCallProfilerTests
{
    [Fact]
    public void RecordsExportCallStats()
    {
        var profiler = new BridgeCallProfiler();
        profiler.OnExportCallStart("Svc", "Method", null);
        profiler.OnExportCallEnd("Svc", "Method", 42, null);

        var stats = profiler.GetMethodStats("Svc", "Method");
        Assert.NotNull(stats);
        Assert.Equal(1, stats.CallCount);
        Assert.Equal(0, stats.ErrorCount);
        Assert.Equal(42, stats.MinLatencyMs);
        Assert.Equal(42, stats.MaxLatencyMs);
        Assert.Equal(42, stats.AvgLatencyMs);
    }

    [Fact]
    public void RecordsImportCallStats()
    {
        var profiler = new BridgeCallProfiler();
        profiler.OnImportCallStart("Svc", "Method", null);
        profiler.OnImportCallEnd("Svc", "Method", 15);

        var stats = profiler.GetMethodStats("Svc", "Method");
        Assert.NotNull(stats);
        Assert.Equal(1, stats.CallCount);
        Assert.Equal(0, stats.ErrorCount);
        Assert.Equal(15, stats.MinLatencyMs);
    }

    [Fact]
    public void RecordsErrorStats()
    {
        var profiler = new BridgeCallProfiler();
        profiler.OnExportCallStart("Svc", "Method", null);
        profiler.OnExportCallError("Svc", "Method", 10, new Exception("test"));

        var stats = profiler.GetMethodStats("Svc", "Method");
        Assert.NotNull(stats);
        Assert.Equal(1, stats.CallCount);
        Assert.Equal(1, stats.ErrorCount);
        Assert.Equal(1.0, stats.ErrorRate);
        Assert.Equal(10, stats.MinLatencyMs);
    }

    [Fact]
    public void RecordsErrorStats_MixedSuccessAndError()
    {
        var profiler = new BridgeCallProfiler();
        profiler.OnExportCallEnd("Svc", "Method", 5, null);
        profiler.OnExportCallEnd("Svc", "Method", 10, null);
        profiler.OnExportCallError("Svc", "Method", 3, new Exception("err"));

        var stats = profiler.GetMethodStats("Svc", "Method");
        Assert.NotNull(stats);
        Assert.Equal(3, stats.CallCount);
        Assert.Equal(1, stats.ErrorCount);
        Assert.Equal(1.0 / 3.0, stats.ErrorRate);
    }

    [Fact]
    public void GetServiceStats_AggregatesAllMethods()
    {
        var profiler = new BridgeCallProfiler();
        profiler.OnExportCallEnd("Svc", "A", 10, null);
        profiler.OnExportCallEnd("Svc", "A", 20, null);
        profiler.OnExportCallEnd("Svc", "B", 30, null);

        var stats = profiler.GetServiceStats("Svc");
        Assert.NotNull(stats);
        Assert.Equal("Svc", stats.ServiceName);
        Assert.Equal(3, stats.TotalCalls);
        Assert.Equal(0, stats.TotalErrors);
        Assert.Equal(2, stats.Methods.Count);
        Assert.Contains(stats.Methods, m => m.MethodName == "A" && m.CallCount == 2);
        Assert.Contains(stats.Methods, m => m.MethodName == "B" && m.CallCount == 1);
    }

    [Fact]
    public void GetSlowestCalls_ReturnsTopN()
    {
        var profiler = new BridgeCallProfiler();
        profiler.OnExportCallEnd("S1", "Fast", 5, null);
        profiler.OnExportCallEnd("S2", "Slow", 100, null);
        profiler.OnExportCallEnd("S3", "Medium", 50, null);

        var slowest = profiler.GetSlowestCalls(2);
        Assert.Equal(2, slowest.Count);
        Assert.Equal("Slow", slowest[0].MethodName);
        Assert.Equal(100, slowest[0].AvgLatencyMs);
        Assert.Equal("Medium", slowest[1].MethodName);
        Assert.Equal(50, slowest[1].AvgLatencyMs);
    }

    [Fact]
    public void Reset_ClearsAllStats()
    {
        var profiler = new BridgeCallProfiler();
        profiler.OnExportCallEnd("Svc", "Method", 42, null);
        Assert.NotNull(profiler.GetMethodStats("Svc", "Method"));

        profiler.Reset();
        Assert.Null(profiler.GetMethodStats("Svc", "Method"));
        Assert.Empty(profiler.GetAllServiceStats());
    }

    [Fact]
    public void ExportToJson_ProducesValidJson()
    {
        var profiler = new BridgeCallProfiler();
        profiler.OnExportCallEnd("Svc", "Method", 42, null);

        var json = profiler.ExportToJson();
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        Assert.StartsWith("[", json);
        Assert.EndsWith("]", json);
        // Parse to verify valid JSON
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, parsed.ValueKind);
    }

    [Fact]
    public void DelegatesToInnerTracer()
    {
        var inner = new RecordingTracer();
        var profiler = new BridgeCallProfiler(inner);

        profiler.OnExportCallStart("Svc", "Method", "{}");
        profiler.OnExportCallEnd("Svc", "Method", 42, null);
        profiler.OnImportCallStart("Svc", "Other", null);
        profiler.OnImportCallEnd("Svc", "Other", 10);

        Assert.Contains(inner.Events, e => e == "ExportStart:Svc.Method");
        Assert.Contains(inner.Events, e => e == "ExportEnd:Svc.Method:42ms");
        Assert.Contains(inner.Events, e => e == "ImportStart:Svc.Other");
        Assert.Contains(inner.Events, e => e == "ImportEnd:Svc.Other:10ms");
    }

    [Fact]
    public void GetMethodStats_ReturnsNull_WhenNoCalls()
    {
        var profiler = new BridgeCallProfiler();
        Assert.Null(profiler.GetMethodStats("Svc", "Method"));
    }

    [Fact]
    public void GetServiceStats_ReturnsNull_WhenNoCalls()
    {
        var profiler = new BridgeCallProfiler();
        Assert.Null(profiler.GetServiceStats("Svc"));
    }

    [Fact]
    public void OnServiceExposed_and_OnServiceRemoved_without_inner_do_not_throw()
    {
        var profiler = new BridgeCallProfiler();
        profiler.OnServiceExposed("Svc", 3, true);
        profiler.OnServiceRemoved("Svc");
    }

    [Fact]
    public void OnServiceExposed_and_OnServiceRemoved_delegate_to_inner()
    {
        var inner = new RecordingTracer();
        var profiler = new BridgeCallProfiler(inner);
        profiler.OnServiceExposed("Svc", 3, true);
        profiler.OnServiceRemoved("Svc");
        Assert.Contains(inner.Events, e => e == "Exposed:Svc:3");
        Assert.Contains(inner.Events, e => e == "Removed:Svc");
    }

    [Fact]
    public void Percentiles_ComputedCorrectly()
    {
        var profiler = new BridgeCallProfiler();
        for (var i = 1; i <= 100; i++)
            profiler.OnExportCallEnd("Svc", "Method", i, null);

        var stats = profiler.GetMethodStats("Svc", "Method");
        Assert.NotNull(stats);
        Assert.Equal(100, stats.CallCount);
        Assert.Equal(1, stats.MinLatencyMs);
        Assert.Equal(100, stats.MaxLatencyMs);
        Assert.True(stats.P50LatencyMs >= 50 && stats.P50LatencyMs <= 51);
        Assert.True(stats.P95LatencyMs >= 95 && stats.P95LatencyMs <= 96);
        Assert.True(stats.P99LatencyMs >= 99 && stats.P99LatencyMs <= 100);
    }

    private sealed class RecordingTracer : IBridgeTracer
    {
        public List<string> Events { get; } = [];

        public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
            => Events.Add($"ExportStart:{serviceName}.{methodName}");

        public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
            => Events.Add($"ExportEnd:{serviceName}.{methodName}:{elapsedMs}ms");

        public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception error)
            => Events.Add($"ExportError:{serviceName}.{methodName}");

        public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
            => Events.Add($"ImportStart:{serviceName}.{methodName}");

        public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
            => Events.Add($"ImportEnd:{serviceName}.{methodName}:{elapsedMs}ms");

        public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
            => Events.Add($"Exposed:{serviceName}:{methodCount}");

        public void OnServiceRemoved(string serviceName)
            => Events.Add($"Removed:{serviceName}");
    }
}
