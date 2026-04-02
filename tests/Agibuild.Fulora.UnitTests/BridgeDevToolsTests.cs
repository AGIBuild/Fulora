using System.Reflection;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class BridgeEventCollectorTests
{
    [Fact]
    public void New_collector_is_empty()
    {
        var collector = new BridgeEventCollector(10);
        Assert.Equal(0, collector.Count);
        Assert.Equal(0, collector.DroppedCount);
        Assert.Equal(10, collector.Capacity);
        Assert.Empty(collector.GetEvents());
    }

    [Fact]
    public void Add_increments_count_and_assigns_sequential_ids()
    {
        var collector = new BridgeEventCollector(10);
        collector.Add(MakeEvent("Svc", "A"));
        collector.Add(MakeEvent("Svc", "B"));

        Assert.Equal(2, collector.Count);
        var events = collector.GetEvents();
        Assert.Equal(0, events[0].Id);
        Assert.Equal(1, events[1].Id);
    }

    [Fact]
    public void Overflow_drops_oldest_and_increments_dropped_count()
    {
        var collector = new BridgeEventCollector(3);
        for (var i = 0; i < 5; i++)
            collector.Add(MakeEvent("S", $"M{i}"));

        Assert.Equal(3, collector.Count);
        Assert.Equal(2, collector.DroppedCount);

        var events = collector.GetEvents();
        Assert.Equal("M2", events[0].MethodName);
        Assert.Equal("M3", events[1].MethodName);
        Assert.Equal("M4", events[2].MethodName);
    }

    [Fact]
    public void Clear_resets_buffer_and_dropped_count()
    {
        var collector = new BridgeEventCollector(3);
        for (var i = 0; i < 5; i++)
            collector.Add(MakeEvent("S", $"M{i}"));

        collector.Clear();
        Assert.Equal(0, collector.Count);
        Assert.Equal(0, collector.DroppedCount);
        Assert.Empty(collector.GetEvents());
    }

    [Fact]
    public void Subscribe_receives_events()
    {
        var collector = new BridgeEventCollector(10);
        var received = new List<BridgeDevToolsEvent>();
        using var sub = collector.Subscribe(e => received.Add(e));

        collector.Add(MakeEvent("Svc", "Method"));
        Assert.Single(received);
        Assert.Equal("Svc", received[0].ServiceName);
    }

    [Fact]
    public void Dispose_subscription_stops_callbacks()
    {
        var collector = new BridgeEventCollector(10);
        var received = new List<BridgeDevToolsEvent>();
        var sub = collector.Subscribe(e => received.Add(e));
        sub.Dispose();

        collector.Add(MakeEvent("Svc", "Method"));
        Assert.Empty(received);
    }

    [Fact]
    public void Subscriber_exception_does_not_break_collector()
    {
        var collector = new BridgeEventCollector(10);
        using var sub = collector.Subscribe(_ => throw new InvalidOperationException("boom"));

        collector.Add(MakeEvent("Svc", "Method"));
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void Capacity_must_be_at_least_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BridgeEventCollector(0));
    }

    private static BridgeDevToolsEvent MakeEvent(string service, string method) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Direction = BridgeCallDirection.Export,
        Phase = BridgeCallPhase.Start,
        ServiceName = service,
        MethodName = method,
    };
}

public class DevToolsPanelTracerTests
{
    [Fact]
    public void Export_call_lifecycle_produces_start_and_end_events()
    {
        var collector = new BridgeEventCollector(100);
        var tracer = new DevToolsPanelTracer(collector);

        tracer.OnExportCallStart("AppService", "getUser", """{"id":1}""");
        tracer.OnExportCallEnd("AppService", "getUser", 42, "UserProfile");

        var events = collector.GetEvents();
        Assert.Equal(2, events.Count);

        Assert.Equal(BridgeCallDirection.Export, events[0].Direction);
        Assert.Equal(BridgeCallPhase.Start, events[0].Phase);
        Assert.Equal("""{"id":1}""", events[0].ParamsJson);

        Assert.Equal(BridgeCallPhase.End, events[1].Phase);
        Assert.Equal(42, events[1].ElapsedMs);
        Assert.Equal("UserProfile", events[1].ResultJson);
    }

    [Fact]
    public void Export_call_error_produces_error_event()
    {
        var collector = new BridgeEventCollector(100);
        var tracer = new DevToolsPanelTracer(collector);

        tracer.OnExportCallError("AppService", "fail", 10, new Exception("test error"));

        var evt = Assert.Single(collector.GetEvents());
        Assert.Equal(BridgeCallPhase.Error, evt.Phase);
        Assert.Equal("test error", evt.ErrorMessage);
    }

    [Fact]
    public void Import_call_lifecycle_produces_events()
    {
        var collector = new BridgeEventCollector(100);
        var tracer = new DevToolsPanelTracer(collector);

        tracer.OnImportCallStart("UiCtrl", "showNotif", null);
        tracer.OnImportCallEnd("UiCtrl", "showNotif", 5);

        var events = collector.GetEvents();
        Assert.Equal(2, events.Count);
        Assert.Equal(BridgeCallDirection.Import, events[0].Direction);
    }

    [Fact]
    public void Service_lifecycle_events_are_captured()
    {
        var collector = new BridgeEventCollector(100);
        var tracer = new DevToolsPanelTracer(collector);

        tracer.OnServiceExposed("AppService", 5, true);
        tracer.OnServiceRemoved("AppService");

        var events = collector.GetEvents();
        Assert.Equal(2, events.Count);
        Assert.Equal(BridgeCallDirection.Lifecycle, events[0].Direction);
        Assert.Equal(BridgeCallPhase.ServiceExposed, events[0].Phase);
        Assert.Equal(BridgeCallPhase.ServiceRemoved, events[1].Phase);
    }

    [Fact]
    public void Inner_tracer_receives_all_calls()
    {
        var collector = new BridgeEventCollector(100);
        var inner = new RecordingTracer();
        var tracer = new DevToolsPanelTracer(collector, inner);

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
    public void NullBridgeTracer_inner_is_skipped()
    {
        var collector = new BridgeEventCollector(100);
        var tracer = new DevToolsPanelTracer(collector, NullBridgeTracer.Instance);

        tracer.OnExportCallStart("S", "M", "{}");
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void Large_payload_is_truncated()
    {
        var collector = new BridgeEventCollector(100);
        var tracer = new DevToolsPanelTracer(collector);

        var largeJson = new string('x', 5000);
        tracer.OnExportCallStart("S", "M", largeJson);

        var evt = Assert.Single(collector.GetEvents());
        Assert.True(evt.Truncated);
        Assert.True(evt.ParamsJson!.Length < 5000);
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
}

public class BridgeDevToolsServiceTests
{
    [Fact]
    public void GetOverlayHtml_returns_non_empty_html()
    {
        var html = BridgeDevToolsService.GetOverlayHtml();
        Assert.NotNull(html);
        Assert.Contains("Bridge DevTools", html);
        Assert.Contains("__bridgeDevToolsAddEvent", html);
    }

    [Fact]
    public void Tracer_feeds_collector()
    {
        using var svc = new BridgeDevToolsService();
        svc.Tracer.OnExportCallStart("Svc", "Method", "{}");

        Assert.Equal(1, svc.Collector.Count);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var svc = new BridgeDevToolsService();
        svc.Dispose();
        svc.Dispose();
    }

    [Fact]
    public void BridgeCallProfiler_emits_unified_diagnostics_event()
    {
        var sink = new MemoryFuloraDiagnosticsSink();
        var profiler = new BridgeCallProfiler(diagnosticsSink: sink);

        profiler.OnImportCallEnd("UiCtrl", "showNotif", 5);

        var diagnostic = Assert.Single(sink.Events);
        Assert.Equal("bridge.import.end", diagnostic.EventName);
        Assert.Equal("bridge", diagnostic.Layer);
        Assert.Equal("BridgeCallProfiler", diagnostic.Component);
        Assert.Equal("UiCtrl", diagnostic.Service);
        Assert.Equal("showNotif", diagnostic.Method);
        Assert.Equal(5, diagnostic.DurationMs);
    }

    [Fact]
    public void DiagnosticsSink_projects_unified_events_into_collector()
    {
        using var svc = new BridgeDevToolsService();

        svc.DiagnosticsSink.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "bridge.export.error",
            Layer = "bridge",
            Component = "TestComponent",
            Service = "AppService",
            Method = "fail",
            DurationMs = 12,
            Status = "error",
            ErrorType = "InvalidOperationException",
            Attributes = new Dictionary<string, string>
            {
                ["message"] = "boom"
            }
        });

        var evt = Assert.Single(svc.Collector.GetEvents());
        Assert.Equal(BridgeCallDirection.Export, evt.Direction);
        Assert.Equal(BridgeCallPhase.Error, evt.Phase);
        Assert.Equal("AppService", evt.ServiceName);
        Assert.Equal("fail", evt.MethodName);
        Assert.Equal(12, evt.ElapsedMs);
        Assert.Equal("boom", evt.ErrorMessage);
    }

    [Theory]
    [InlineData("bridge.export.start", BridgeCallDirection.Export, BridgeCallPhase.Start)]
    [InlineData("bridge.import.start", BridgeCallDirection.Import, BridgeCallPhase.Start)]
    [InlineData("bridge.import.end", BridgeCallDirection.Import, BridgeCallPhase.End)]
    [InlineData("bridge.service.exposed", BridgeCallDirection.Lifecycle, BridgeCallPhase.ServiceExposed)]
    [InlineData("bridge.service.removed", BridgeCallDirection.Lifecycle, BridgeCallPhase.ServiceRemoved)]
    public void DiagnosticsSink_maps_supported_event_names(string eventName, BridgeCallDirection direction, BridgeCallPhase phase)
    {
        using var svc = new BridgeDevToolsService();

        svc.DiagnosticsSink.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = eventName,
            Layer = "bridge",
            Component = "ComponentFallback",
            Status = "ok"
        });

        var evt = Assert.Single(svc.Collector.GetEvents());
        Assert.Equal(direction, evt.Direction);
        Assert.Equal(phase, evt.Phase);
        Assert.Equal("ComponentFallback", evt.ServiceName);
        Assert.Equal(string.Empty, evt.MethodName);
    }

    [Fact]
    public void DiagnosticsSink_ignores_unsupported_event_names()
    {
        using var svc = new BridgeDevToolsService();

        svc.DiagnosticsSink.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "runtime.navigation.start",
            Layer = "runtime",
            Component = "WebViewCore",
            Status = "started"
        });

        Assert.Empty(svc.Collector.GetEvents());
    }

    [Fact]
    public void DiagnosticsSink_maps_export_end_and_copies_result_and_params()
    {
        using var svc = new BridgeDevToolsService();

        svc.DiagnosticsSink.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "bridge.export.end",
            Layer = "bridge",
            Component = "BridgeRuntime",
            Service = "AppService",
            Method = "getCurrentUser",
            DurationMs = 18,
            Status = "success",
            Attributes = new Dictionary<string, string>
            {
                ["resultType"] = "UserDto",
                ["params"] = """{"id":1}"""
            }
        });

        var evt = Assert.Single(svc.Collector.GetEvents());
        Assert.Equal(BridgeCallDirection.Export, evt.Direction);
        Assert.Equal(BridgeCallPhase.End, evt.Phase);
        Assert.Equal("AppService", evt.ServiceName);
        Assert.Equal("getCurrentUser", evt.MethodName);
        Assert.Equal(18, evt.ElapsedMs);
        Assert.Equal("UserDto", evt.ResultJson);
        Assert.Equal("""{"id":1}""", evt.ParamsJson);
    }

    [Fact]
    public void DiagnosticsSink_uses_error_type_when_message_attribute_is_missing()
    {
        using var svc = new BridgeDevToolsService();

        svc.DiagnosticsSink.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "bridge.export.error",
            Layer = "bridge",
            Component = "BridgeRuntime",
            Service = "AppService",
            Method = "save",
            DurationMs = 7,
            Status = "error",
            ErrorType = "InvalidOperationException",
            Attributes = new Dictionary<string, string>()
        });

        var evt = Assert.Single(svc.Collector.GetEvents());
        Assert.Equal("InvalidOperationException", evt.ErrorMessage);
    }

    [Fact]
    public void DiagnosticsSink_ignores_known_prefix_with_unknown_phase()
    {
        using var svc = new BridgeDevToolsService();

        svc.DiagnosticsSink.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "bridge.export.cancelled",
            Layer = "bridge",
            Component = "BridgeRuntime",
            Status = "cancelled"
        });

        Assert.Empty(svc.Collector.GetEvents());
    }

    [Fact]
    public void DiagnosticsSink_on_null_event_throws()
    {
        using var svc = new BridgeDevToolsService();
        Assert.Throws<ArgumentNullException>(() => svc.DiagnosticsSink.OnEvent(null!));
    }

    [Fact]
    public void CollectorDiagnosticsSink_ctor_null_collector_throws()
    {
        var nestedType = typeof(BridgeDevToolsService)
            .GetNestedType("CollectorDiagnosticsSink", BindingFlags.NonPublic);
        Assert.NotNull(nestedType);

        var ctor = nestedType!.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, [typeof(BridgeEventCollector)], null);
        Assert.NotNull(ctor);

        var ex = Assert.Throws<TargetInvocationException>(() => ctor!.Invoke([null]));
        var inner = Assert.IsType<ArgumentNullException>(ex.InnerException);
        Assert.Equal("collector", inner.ParamName);
    }

    [Fact]
    public void StartPushing_sends_existing_events_and_subscribes()
    {
        using var svc = new BridgeDevToolsService();
        svc.Tracer.OnExportCallStart("Svc", "A", null);

        var scripts = new List<string>();
        svc.StartPushing(script => { scripts.Add(script); return Task.FromResult<string?>(null); });

        Assert.Contains(scripts, s => s.Contains("__bridgeDevToolsLoadEvents"));

        svc.Tracer.OnImportCallStart("Svc", "B", null);
        Assert.Contains(scripts, s => s.Contains("__bridgeDevToolsAddEvent"));
    }

    [Fact]
    public void StartPushing_no_existing_events_skips_load_batch()
    {
        using var svc = new BridgeDevToolsService();
        var scripts = new List<string>();
        svc.StartPushing(script => { scripts.Add(script); return Task.FromResult<string?>(null); });

        Assert.DoesNotContain(scripts, s => s.Contains("__bridgeDevToolsLoadEvents"));
    }

    [Fact]
    public void StopPushing_stops_callback()
    {
        using var svc = new BridgeDevToolsService();
        var scripts = new List<string>();
        svc.StartPushing(script => { scripts.Add(script); return Task.FromResult<string?>(null); });
        svc.StopPushing();

        svc.Tracer.OnExportCallStart("Svc", "A", null);
        Assert.Empty(scripts);
    }

    [Fact]
    public void StartPushing_null_throws()
    {
        using var svc = new BridgeDevToolsService();
        Assert.Throws<ArgumentNullException>(() => svc.StartPushing(null!));
    }

    [Fact]
    public void Dispose_stops_subscription()
    {
        var svc = new BridgeDevToolsService();
        var scripts = new List<string>();
        svc.StartPushing(script => { scripts.Add(script); return Task.FromResult<string?>(null); });
        svc.Dispose();

        svc.Tracer.OnExportCallStart("Svc", "A", null);
        Assert.Empty(scripts);
    }

    [Fact]
    public void StartPushing_twice_disposes_previous_subscription()
    {
        using var svc = new BridgeDevToolsService();
        var scripts1 = new List<string>();
        svc.StartPushing(script => { scripts1.Add(script); return Task.FromResult<string?>(null); });

        var scripts2 = new List<string>();
        svc.StartPushing(script => { scripts2.Add(script); return Task.FromResult<string?>(null); });

        svc.Tracer.OnExportCallStart("S", "M", null);
        Assert.Empty(scripts1);
        Assert.Single(scripts2);
    }
}

public class BridgeEventCollectorSubscriptionTests
{
    [Fact]
    public void Dispose_one_subscription_keeps_others()
    {
        var collector = new BridgeEventCollector(10);
        var received1 = new List<BridgeDevToolsEvent>();
        var received2 = new List<BridgeDevToolsEvent>();
        var sub1 = collector.Subscribe(e => received1.Add(e));
        using var sub2 = collector.Subscribe(e => received2.Add(e));

        sub1.Dispose();

        collector.Add(new BridgeDevToolsEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Direction = BridgeCallDirection.Export,
            Phase = BridgeCallPhase.Start,
            ServiceName = "S",
            MethodName = "M",
        });

        Assert.Empty(received1);
        Assert.Single(received2);
    }
}

public class DevToolsPanelTracerConstructorTests
{
    [Fact]
    public void Null_collector_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DevToolsPanelTracer(null!));
    }
}
