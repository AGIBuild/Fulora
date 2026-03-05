using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class CompositeBridgeTracerTests
{
    [Fact]
    public void CompositeBridgeTracer_ForwardsToAll()
    {
        var t1 = new RecordingTracer();
        var t2 = new RecordingTracer();
        var composite = new CompositeBridgeTracer(t1, t2);

        composite.OnExportCallStart("Svc", "Method", "{}");
        composite.OnExportCallEnd("Svc", "Method", 42, null);
        composite.OnImportCallStart("Svc", "Other", null);
        composite.OnImportCallEnd("Svc", "Other", 10);
        composite.OnServiceExposed("Svc", 2, true);
        composite.OnServiceRemoved("Svc");

        Assert.Contains(t1.Events, e => e == "ExportStart:Svc.Method");
        Assert.Contains(t1.Events, e => e == "ExportEnd:Svc.Method:42ms");
        Assert.Contains(t1.Events, e => e == "ImportStart:Svc.Other");
        Assert.Contains(t1.Events, e => e == "ImportEnd:Svc.Other:10ms");
        Assert.Contains(t1.Events, e => e.StartsWith("Exposed:Svc:"));
        Assert.Contains(t1.Events, e => e == "Removed:Svc");

        Assert.Contains(t2.Events, e => e == "ExportStart:Svc.Method");
        Assert.Contains(t2.Events, e => e == "ExportEnd:Svc.Method:42ms");
        Assert.Contains(t2.Events, e => e == "ImportStart:Svc.Other");
        Assert.Contains(t2.Events, e => e == "ImportEnd:Svc.Other:10ms");
        Assert.Contains(t2.Events, e => e.StartsWith("Exposed:Svc:"));
        Assert.Contains(t2.Events, e => e == "Removed:Svc");
    }

    [Fact]
    public void CompositeBridgeTracer_IgnoresNullBridgeTracer()
    {
        var t1 = new RecordingTracer();
        var composite = new CompositeBridgeTracer(t1, NullBridgeTracer.Instance);

        composite.OnExportCallStart("Svc", "Method", null);
        composite.OnExportCallEnd("Svc", "Method", 42, null);

        Assert.Equal(2, t1.Events.Count);
        Assert.Contains(t1.Events, e => e == "ExportStart:Svc.Method");
        Assert.Contains(t1.Events, e => e == "ExportEnd:Svc.Method:42ms");
    }

    [Fact]
    public void CompositeBridgeTracer_IsolatesExceptions_AllMethods()
    {
        var good = new RecordingTracer();
        var bad = new ThrowingTracer();
        var composite = new CompositeBridgeTracer(good, bad);

        composite.OnExportCallStart("Svc", "Method", null);
        composite.OnExportCallEnd("Svc", "Method", 42, null);
        composite.OnExportCallError("Svc", "Method", 1, new Exception("test"));
        composite.OnImportCallStart("Svc", "Other", null);
        composite.OnImportCallEnd("Svc", "Other", 10);
        composite.OnServiceExposed("Svc", 2, true);
        composite.OnServiceRemoved("Svc");

        Assert.Contains(good.Events, e => e == "ExportStart:Svc.Method");
        Assert.Contains(good.Events, e => e == "ExportEnd:Svc.Method:42ms");
        Assert.Contains(good.Events, e => e == "ExportError:Svc.Method");
        Assert.Contains(good.Events, e => e == "ImportStart:Svc.Other");
        Assert.Contains(good.Events, e => e == "ImportEnd:Svc.Other:10ms");
        Assert.Contains(good.Events, e => e.StartsWith("Exposed:Svc:"));
        Assert.Contains(good.Events, e => e == "Removed:Svc");
    }

    [Fact]
    public void CompositeBridgeTracer_EnumerableConstructor()
    {
        var tracers = new List<IBridgeTracer> { new RecordingTracer(), new RecordingTracer() };
        var composite = new CompositeBridgeTracer(tracers);

        composite.OnExportCallStart("Svc", "M", null);
        composite.OnExportCallEnd("Svc", "M", 1, null);

        var r1 = (RecordingTracer)tracers[0];
        var r2 = (RecordingTracer)tracers[1];
        Assert.Contains(r1.Events, e => e == "ExportEnd:Svc.M:1ms");
        Assert.Contains(r2.Events, e => e == "ExportEnd:Svc.M:1ms");
    }

    [Fact]
    public void CompositeBridgeTracer_EmptyArray_DoesNotThrow()
    {
        var composite = new CompositeBridgeTracer();
        composite.OnExportCallStart("Svc", "M", null);
        composite.OnExportCallEnd("Svc", "M", 1, null);
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

    private sealed class ThrowingTracer : IBridgeTracer
    {
        public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
            => throw new InvalidOperationException("test throw");

        public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
            => throw new InvalidOperationException("test throw");

        public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception error)
            => throw new InvalidOperationException("test throw");

        public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
            => throw new InvalidOperationException("test throw");

        public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
            => throw new InvalidOperationException("test throw");

        public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
            => throw new InvalidOperationException("test throw");

        public void OnServiceRemoved(string serviceName)
            => throw new InvalidOperationException("test throw");
    }
}
