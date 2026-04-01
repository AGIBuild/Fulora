using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class TelemetryTests
{
    [Fact]
    public void NullTelemetryProvider_does_not_throw()
    {
        var provider = NullTelemetryProvider.Instance;
        provider.TrackEvent("test");
        provider.TrackEvent("test", new Dictionary<string, string> { ["k"] = "v" });
        provider.TrackMetric("latency", 42.5);
        provider.TrackMetric("latency", 42.5, new Dictionary<string, string> { ["dim"] = "x" });
        provider.TrackException(new InvalidOperationException("test"));
        provider.TrackException(new InvalidOperationException("test"), new Dictionary<string, string> { ["k"] = "v" });
        provider.Flush();
    }

    [Fact]
    public void NullTelemetryProvider_Instance_is_singleton()
    {
        var a = NullTelemetryProvider.Instance;
        var b = NullTelemetryProvider.Instance;
        Assert.Same(a, b);
    }

    [Fact]
    public void BridgeTelemetryTracer_tracks_metrics_on_export_call_end()
    {
        var events = new List<(string Name, double Value, IDictionary<string, string>? Dims)>();
        var provider = new RecordingTelemetryProvider(
            onMetric: (n, v, d) => events.Add((n, v, d)));

        var tracer = new BridgeTelemetryTracer(provider);
        tracer.OnExportCallEnd("AppService", "getUser", 42, "UserProfile");

        var metric = Assert.Single(events);
        Assert.Contains("export.latency_ms", metric.Name);
        Assert.Equal(42, metric.Value);
        Assert.NotNull(metric.Dims);
        Assert.Equal("AppService", metric.Dims["service"]);
        Assert.Equal("getUser", metric.Dims["method"]);
    }

    [Fact]
    public void BridgeTelemetryTracer_emits_unified_diagnostics_event()
    {
        var sink = new MemoryFuloraDiagnosticsSink();
        var tracer = new BridgeTelemetryTracer(NullTelemetryProvider.Instance, diagnosticsSink: sink);

        tracer.OnExportCallEnd("AppService", "getUser", 42, "UserProfile");

        var diagnostic = Assert.Single(sink.Events);
        Assert.Equal("bridge.export.end", diagnostic.EventName);
        Assert.Equal("bridge", diagnostic.Layer);
        Assert.Equal("BridgeTelemetryTracer", diagnostic.Component);
        Assert.Equal("AppService", diagnostic.Service);
        Assert.Equal("getUser", diagnostic.Method);
        Assert.Equal(42, diagnostic.DurationMs);
        Assert.Equal("success", diagnostic.Status);
    }

    [Fact]
    public void BridgeTelemetryTracer_export_start_with_payload_emits_payload_attribute()
    {
        var sink = new MemoryFuloraDiagnosticsSink();
        var tracer = new BridgeTelemetryTracer(NullTelemetryProvider.Instance, diagnosticsSink: sink);

        tracer.OnExportCallStart("AppService", "getUser", """{"id":1}""");

        var diagnostic = Assert.Single(sink.Events);
        Assert.Equal("bridge.export.start", diagnostic.EventName);
        Assert.Equal("""{"id":1}""", diagnostic.Attributes["params"]);
    }

    [Fact]
    public void BridgeTelemetryTracer_import_start_with_whitespace_payload_omits_params_attribute()
    {
        var sink = new MemoryFuloraDiagnosticsSink();
        var tracer = new BridgeTelemetryTracer(NullTelemetryProvider.Instance, diagnosticsSink: sink);

        tracer.OnImportCallStart("UiService", "notify", "   ");

        var diagnostic = Assert.Single(sink.Events);
        Assert.Equal("bridge.import.start", diagnostic.EventName);
        Assert.Empty(diagnostic.Attributes);
    }

    [Fact]
    public void BridgeTelemetryTracer_service_lifecycle_emits_registration_mode_and_removed_status()
    {
        var sink = new MemoryFuloraDiagnosticsSink();
        var tracer = new BridgeTelemetryTracer(NullTelemetryProvider.Instance, diagnosticsSink: sink);

        tracer.OnServiceExposed("AppService", 2, true);
        tracer.OnServiceRemoved("AppService");

        Assert.Equal(2, sink.Events.Count);
        Assert.Equal("source-generated", sink.Events[0].Attributes["registrationMode"]);
        Assert.Equal("removed", sink.Events[1].Status);
    }

    [Fact]
    public void LoggingFuloraDiagnosticsSink_uses_information_for_non_error_status()
    {
        var logger = new RecordingLogger();
        var sink = new LoggingFuloraDiagnosticsSink(logger);

        sink.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "bridge.export.start",
            Layer = "bridge",
            Component = "BridgeTelemetryTracer",
            Service = "AppService",
            Method = "getUser",
            Status = "started",
            DurationMs = 5
        });

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, entry.Level);
        Assert.Contains("bridge.export.start", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoggingFuloraDiagnosticsSink_uses_warning_for_error_and_dropped_statuses()
    {
        var logger = new RecordingLogger();
        var sink = new LoggingFuloraDiagnosticsSink(logger);

        sink.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "bridge.export.error",
            Layer = "bridge",
            Component = "BridgeTelemetryTracer",
            Service = "AppService",
            Method = "getUser",
            Status = "error",
            ErrorType = "InvalidOperationException"
        });

        sink.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "runtime.message.dropped",
            Layer = "runtime",
            Component = "WebViewCore",
            Status = "dropped"
        });

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level));
    }

    [Fact]
    public void LoggingFuloraDiagnosticsSink_throws_on_null_inputs()
    {
        Assert.Throws<ArgumentNullException>(() => new LoggingFuloraDiagnosticsSink(null!));

        var sink = new LoggingFuloraDiagnosticsSink(new RecordingLogger());
        Assert.Throws<ArgumentNullException>(() => sink.OnEvent(null!));
    }

    [Fact]
    public void BridgeTelemetryTracer_tracks_exception_on_export_call_error()
    {
        var metrics = new List<(string Name, double Value, IDictionary<string, string>? Dims)>();
        var evts = new List<(string Name, IDictionary<string, string>? Props)>();
        var exceptions = new List<(Exception Ex, IDictionary<string, string>? Props)>();
        var provider = new RecordingTelemetryProvider(
            onMetric: (n, v, d) => metrics.Add((n, v, d)),
            onEvent: (n, p) => evts.Add((n, p)),
            onException: (e, p) => exceptions.Add((e, p)));

        var tracer = new BridgeTelemetryTracer(provider);
        var ex = new InvalidOperationException("bridge error");
        tracer.OnExportCallError("Calc", "divide", 10, ex);

        var metric = Assert.Single(metrics);
        Assert.Equal(10, metric.Value);

        var evt = Assert.Single(evts);
        Assert.Contains("export.error", evt.Name);
        Assert.Equal("bridge error", evt.Props?["error"]);

        var captured = Assert.Single(exceptions);
        Assert.Same(ex, captured.Ex);
    }

    [Fact]
    public void CompositeTelemetryProvider_dispatches_to_all_inner_providers()
    {
        var metrics1 = new List<(string Name, double Value, IDictionary<string, string>? Dims)>();
        var metrics2 = new List<(string Name, double Value, IDictionary<string, string>? Dims)>();
        var p1 = new RecordingTelemetryProvider(onMetric: (n, v, d) => metrics1.Add((n, v, d)));
        var p2 = new RecordingTelemetryProvider(onMetric: (n, v, d) => metrics2.Add((n, v, d)));

        var composite = new CompositeTelemetryProvider(p1, p2);
        composite.TrackMetric("latency", 99.5, new Dictionary<string, string> { ["dim"] = "x" });

        var m1 = Assert.Single(metrics1);
        Assert.Equal("latency", m1.Name);
        Assert.Equal(99.5, m1.Value);
        Assert.Equal("x", m1.Dims?["dim"]);

        var m2 = Assert.Single(metrics2);
        Assert.Equal("latency", m2.Name);
        Assert.Equal(99.5, m2.Value);
    }

    [Fact]
    public void CompositeTelemetryProvider_exception_in_one_provider_does_not_affect_others()
    {
        var received = new List<string>();
        var good = new RecordingTelemetryProvider(onEvent: (n, _) => received.Add(n));
        var bad = new ThrowingTelemetryProvider();
        var composite = new CompositeTelemetryProvider(good, bad);

        composite.TrackEvent("test"); // should not throw; good provider receives it

        Assert.Single(received);
        Assert.Equal("test", received[0]);
    }

    [Fact]
    public void CompositeTelemetryProvider_with_empty_array_does_not_throw()
    {
        var composite = new CompositeTelemetryProvider();
        composite.TrackEvent("e");
        composite.TrackMetric("m", 1.0);
        composite.TrackException(new Exception("x"));
        composite.Flush();
    }

    [Fact]
    public void ConsoleTelemetryProvider_does_not_throw()
    {
        var provider = new ConsoleTelemetryProvider();
        provider.TrackEvent("test");
        provider.TrackEvent("test", new Dictionary<string, string> { ["k"] = "v" });
        provider.TrackMetric("latency", 42.5);
        provider.TrackMetric("latency", 42.5, new Dictionary<string, string> { ["dim"] = "x" });
        provider.TrackException(new InvalidOperationException("test"));
        provider.TrackException(new InvalidOperationException("test"), new Dictionary<string, string> { ["k"] = "v" });
        provider.Flush();
    }

    [Fact]
    public void BridgeTelemetryTracer_tracks_import_call_metrics()
    {
        var metrics = new List<(string Name, double Value, IDictionary<string, string>? Dims)>();
        var provider = new RecordingTelemetryProvider(onMetric: (n, v, d) => metrics.Add((n, v, d)));

        var tracer = new BridgeTelemetryTracer(provider);
        tracer.OnImportCallEnd("JsService", "invoke", 15);

        var metric = Assert.Single(metrics);
        Assert.Contains("import.latency_ms", metric.Name);
        Assert.Equal(15, metric.Value);
        Assert.NotNull(metric.Dims);
        Assert.Equal("JsService", metric.Dims["service"]);
        Assert.Equal("invoke", metric.Dims["method"]);
    }

    [Fact]
    public void BridgeTelemetryTracer_inner_tracer_receives_all_calls()
    {
        var inner = new RecordingTracer();
        var provider = NullTelemetryProvider.Instance;
        var tracer = new BridgeTelemetryTracer(provider, inner);

        tracer.OnExportCallStart("S", "M", "{}");
        tracer.OnExportCallEnd("S", "M", 1, "void");
        tracer.OnExportCallError("S", "M", 2, new Exception("e"));
        tracer.OnImportCallStart("S", "M", null);
        tracer.OnImportCallEnd("S", "M", 3);
        tracer.OnServiceExposed("S", 1, false);
        tracer.OnServiceRemoved("S");

        Assert.Equal(7, inner.Calls.Count);
    }

    [Fact]
    public void CompositeTelemetryProvider_TrackException_swallows_inner_errors()
    {
        var received = new List<string>();
        var good = new RecordingTelemetryProvider(onException: (ex, _) => received.Add(ex.Message));
        var bad = new ThrowingTelemetryProvider();
        var composite = new CompositeTelemetryProvider(good, bad);

        composite.TrackException(new Exception("test_ex"));
        Assert.Single(received);
        Assert.Equal("test_ex", received[0]);
    }

    [Fact]
    public void CompositeTelemetryProvider_TrackMetric_swallows_inner_errors()
    {
        var received = new List<double>();
        var good = new RecordingTelemetryProvider(onMetric: (_, v, _) => received.Add(v));
        var bad = new ThrowingTelemetryProvider();
        var composite = new CompositeTelemetryProvider(good, bad);

        composite.TrackMetric("m", 3.14);
        Assert.Single(received);
        Assert.Equal(3.14, received[0]);
    }

    [Fact]
    public void CompositeTelemetryProvider_Flush_swallows_inner_errors()
    {
        var bad = new ThrowingTelemetryProvider();
        var composite = new CompositeTelemetryProvider(bad);
        composite.Flush(); // should not throw
    }

    [Fact]
    public void CompositeTelemetryProvider_null_array_does_not_throw()
    {
        var composite = new CompositeTelemetryProvider(null!);
        composite.TrackEvent("e");
    }

    [Fact]
    public void BridgeTelemetryTracer_null_provider_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BridgeTelemetryTracer(null!));
    }

    [Fact]
    public void BridgeTelemetryTracer_NullBridgeTracer_inner_is_stripped()
    {
        var provider = NullTelemetryProvider.Instance;
        var tracer = new BridgeTelemetryTracer(provider, NullBridgeTracer.Instance);

        tracer.OnExportCallStart("S", "M", "{}");
        tracer.OnImportCallStart("S", "M", null);
        tracer.OnServiceExposed("S", 1, false);
        tracer.OnServiceRemoved("S");
    }

    [Fact]
    public void BridgeTelemetryTracer_without_inner_does_not_throw()
    {
        var metrics = new List<string>();
        var provider = new RecordingTelemetryProvider(onMetric: (n, _, _) => metrics.Add(n));
        var tracer = new BridgeTelemetryTracer(provider);

        tracer.OnExportCallStart("S", "M", "{}");
        tracer.OnExportCallEnd("S", "M", 5, "int");
        tracer.OnExportCallError("S", "M", 2, new Exception("e"));
        tracer.OnImportCallStart("S", "M", null);
        tracer.OnImportCallEnd("S", "M", 3);
        tracer.OnServiceExposed("S", 1, false);
        tracer.OnServiceRemoved("S");

        Assert.Equal(3, metrics.Count);
    }

    private sealed class RecordingTelemetryProvider : ITelemetryProvider
    {
        private readonly Action<string, double, IDictionary<string, string>?>? _onMetric;
        private readonly Action<string, IDictionary<string, string>?>? _onEvent;
        private readonly Action<Exception, IDictionary<string, string>?>? _onException;

        public RecordingTelemetryProvider(
            Action<string, double, IDictionary<string, string>?>? onMetric = null,
            Action<string, IDictionary<string, string>?>? onEvent = null,
            Action<Exception, IDictionary<string, string>?>? onException = null)
        {
            _onMetric = onMetric;
            _onEvent = onEvent;
            _onException = onException;
        }

        public void TrackEvent(string name, IDictionary<string, string>? properties = null)
            => _onEvent?.Invoke(name, properties ?? new Dictionary<string, string>());

        public void TrackMetric(string name, double value, IDictionary<string, string>? dimensions = null)
            => _onMetric?.Invoke(name, value, dimensions ?? new Dictionary<string, string>());

        public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
            => _onException?.Invoke(exception, properties ?? new Dictionary<string, string>());

        public void Flush() { }
    }

    private sealed class ThrowingTelemetryProvider : ITelemetryProvider
    {
        public void TrackEvent(string name, IDictionary<string, string>? properties = null)
            => throw new InvalidOperationException("ThrowingTelemetryProvider");
        public void TrackMetric(string name, double value, IDictionary<string, string>? dimensions = null)
            => throw new InvalidOperationException("ThrowingTelemetryProvider");
        public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
            => throw new InvalidOperationException("ThrowingTelemetryProvider");
        public void Flush() => throw new InvalidOperationException("ThrowingTelemetryProvider");
    }

    private sealed class RecordingTracer : IBridgeTracer
    {
        public List<string> Calls { get; } = [];
        public void OnExportCallStart(string s, string m, string? p) => Calls.Add($"ExportStart:{s}.{m}");
        public void OnExportCallEnd(string s, string m, long e, string? r) => Calls.Add($"ExportEnd:{s}.{m}");
        public void OnExportCallError(string s, string m, long e, Exception ex) => Calls.Add($"ExportError:{s}.{m}");
        public void OnImportCallStart(string s, string m, string? p) => Calls.Add($"ImportStart:{s}.{m}");
        public void OnImportCallEnd(string s, string m, long e) => Calls.Add($"ImportEnd:{s}.{m}");
        public void OnServiceExposed(string s, int c, bool g) => Calls.Add($"Exposed:{s}");
        public void OnServiceRemoved(string s) => Calls.Add($"Removed:{s}");
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NoopDisposable.Instance;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
