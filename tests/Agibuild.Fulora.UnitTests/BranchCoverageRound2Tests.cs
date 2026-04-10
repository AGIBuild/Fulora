using System.Reflection;
using System.Text.Json;
using Agibuild.Fulora.Shell;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class BranchCoverageRound2Tests
{
    #region LoggingBridgeTracer edge cases

    [Fact]
    public void LoggingBridgeTracer_OnExportCallEnd_null_resultType_uses_void()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        var tracer = new LoggingBridgeTracer(logger);
        tracer.OnExportCallEnd("Svc", "Method", 10, resultType: null);
    }

    [Fact]
    public void LoggingBridgeTracer_OnExportCallEnd_with_resultType()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        var tracer = new LoggingBridgeTracer(logger);
        tracer.OnExportCallEnd("Svc", "Method", 10, resultType: "string");
    }

    [Fact]
    public void LoggingBridgeTracer_OnServiceExposed_reflection_mode()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        var tracer = new LoggingBridgeTracer(logger);
        tracer.OnServiceExposed("Svc", 3, isSourceGenerated: false);
    }

    [Fact]
    public void LoggingBridgeTracer_OnServiceExposed_source_generated_mode()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        var tracer = new LoggingBridgeTracer(logger);
        tracer.OnServiceExposed("Svc", 3, isSourceGenerated: true);
    }

    [Fact]
    public void LoggingBridgeTracer_Truncate_long_string()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        var tracer = new LoggingBridgeTracer(logger);
        var longParams = new string('x', 300);
        tracer.OnExportCallStart("Svc", "Method", longParams);
    }

    [Fact]
    public void LoggingBridgeTracer_Truncate_short_string()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        var tracer = new LoggingBridgeTracer(logger);
        tracer.OnExportCallStart("Svc", "Method", "short");
    }

    [Fact]
    public void LoggingBridgeTracer_Truncate_null_string()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        var tracer = new LoggingBridgeTracer(logger);
        tracer.OnExportCallStart("Svc", "Method", paramsJson: null);
    }

    #endregion

    #region MockBridgeService GetExposedImplementation

    [Fact]
    public void MockBridgeService_GetExposedImplementation_returns_null_when_not_exposed()
    {
        var bridge = new MockBridgeService();
        Assert.Null(bridge.GetExposedImplementation<IDisposable>());
    }

    [Fact]
    public void MockBridgeService_GetExposedImplementation_returns_impl_when_exposed()
    {
        var bridge = new MockBridgeService();
        var impl = new FakeDisposable();
        bridge.Expose(impl);
        Assert.Same(impl, bridge.GetExposedImplementation<FakeDisposable>());
    }

    #endregion

    #region WebAuthCallbackMatcher relative URI

    [Fact]
    public void WebAuthCallbackMatcher_relative_expected_returns_false()
    {
        var relUri = new Uri("/relative", UriKind.Relative);
        var absUri = new Uri("https://example.com/callback");
        Assert.False(WebAuthCallbackMatcher.IsStrictMatch(relUri, absUri));
    }

    [Fact]
    public void WebAuthCallbackMatcher_relative_actual_returns_false()
    {
        var absUri = new Uri("https://example.com/callback");
        var relUri = new Uri("/relative", UriKind.Relative);
        Assert.False(WebAuthCallbackMatcher.IsStrictMatch(absUri, relUri));
    }

    #endregion

    #region WebViewShellActivationCoordinator whitespace identity

    [Fact]
    public void Activation_Register_whitespace_only_identity_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WebViewShellActivationCoordinator.Register("   ", (_, _) => Task.CompletedTask));
    }

    #endregion

    #region WebViewHostCapabilityBridge null provider

    [Fact]
    public void HostCapabilityBridge_null_provider_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WebViewHostCapabilityBridge(null!));
    }

    #endregion

    #region WebViewHostCapabilityBridge metadata validation

    [Fact]
    public void HostCapabilityBridge_metadata_exceeds_max_entries_denied()
    {
        var provider = new TrackingProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);

        var metadata = new Dictionary<string, string>();
        for (int i = 0; i < 100; i++)
            metadata[$"key-{i}"] = "value";

        var request = new WebViewSystemIntegrationEventRequest
        {
            Source = "test",
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "item",
            Metadata = metadata
        };
        var result = bridge.DispatchSystemIntegrationEvent(request, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, result.Outcome);
    }

    [Fact]
    public void HostCapabilityBridge_metadata_null_value_denied()
    {
        var provider = new TrackingProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);

        var metadata = new Dictionary<string, string> { ["key"] = null! };

        var request = new WebViewSystemIntegrationEventRequest
        {
            Source = "test",
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "item",
            Metadata = metadata
        };
        var result = bridge.DispatchSystemIntegrationEvent(request, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, result.Outcome);
    }

    [Fact]
    public void HostCapabilityBridge_unsupported_event_kind_throws()
    {
        var provider = new TrackingProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);

        var request = new WebViewSystemIntegrationEventRequest
        {
            Source = "test",
            Kind = (WebViewSystemIntegrationEventKind)999,
            ItemId = "item"
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            bridge.DispatchSystemIntegrationEvent(request, Guid.NewGuid()));
    }

    #endregion

    #region WebViewHostCapabilityDiagnosticEventArgs ToKebabCase empty

    [Fact]
    public void DiagnosticEventArgs_ToKebabCase_empty_value_returns_empty()
    {
        var method = typeof(WebViewHostCapabilityDiagnosticEventArgs).GetMethod(
            "ToKebabCase",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [""]);
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region RuntimeBridgeService BridgeImportProxy

    [Fact]
    public void BridgeImportProxy_no_args_invokes_without_params()
    {
        var rpc = new TestableRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            s => Task.FromResult<string?>("ok"),
            NullLogger.Instance);

        var proxy = bridge.GetProxy<IProbeImport>();

        _ = proxy.FireAsync();
        Assert.Contains("ProbeImport.fireAsync", rpc.InvokedMethods);
    }

    [Fact]
    public void BridgeImportProxy_with_args_invokes_with_params()
    {
        var rpc = new TestableRpcService();
        var bridge = new RuntimeBridgeService(
            rpc,
            s => Task.FromResult<string?>("ok"),
            NullLogger.Instance);

        var proxy = bridge.GetProxy<IProbeImport>();

        _ = proxy.GetValueAsync("key", 42);
        Assert.Contains("ProbeImport.getValueAsync", rpc.InvokedMethods);
    }

    [Fact]
    public void BridgeImportProxy_non_task_return_throws()
    {
        var proxy = DispatchProxy.Create<ISyncReturnImport, BridgeImportProxy>();
        var bridgeProxy = (BridgeImportProxy)(object)proxy;

        var initMethod = typeof(BridgeImportProxy).GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(initMethod);

        var rpc = new TestableRpcService();
        initMethod!.Invoke(bridgeProxy, [rpc, "SyncReturnImport"]);

        Assert.Throws<NotSupportedException>(() => proxy.GetValue());
    }

    #endregion

    #region RuntimeBridgeService GetServiceName reflection paths

    [Fact]
    public void GetServiceName_strips_I_prefix_for_standard_interface()
    {
        var method = typeof(RuntimeBridgeService).GetMethod(
            "GetServiceName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var closed = method!.MakeGenericMethod(typeof(BridgeRegistrationAttribute));
        var result = closed.Invoke(null, [typeof(IStandardInterface)]);
        Assert.Equal("StandardInterface", result);
    }

    [Fact]
    public void GetServiceName_keeps_name_without_I_prefix()
    {
        var method = typeof(RuntimeBridgeService).GetMethod(
            "GetServiceName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var closed = method!.MakeGenericMethod(typeof(BridgeRegistrationAttribute));
        var result = closed.Invoke(null, [typeof(NoIPrefixService)]);
        Assert.Equal("NoIPrefixService", result);
    }

    [Fact]
    public void GetServiceName_single_I_is_kept()
    {
        var method = typeof(RuntimeBridgeService).GetMethod(
            "GetServiceName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var closed = method!.MakeGenericMethod(typeof(BridgeRegistrationAttribute));
        var result = closed.Invoke(null, [typeof(I)]);
        Assert.Equal("I", result);
    }

    [Fact]
    public void GetServiceName_Ia_lowercase_second_char_kept()
    {
        var method = typeof(RuntimeBridgeService).GetMethod(
            "GetServiceName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var closed = method!.MakeGenericMethod(typeof(BridgeRegistrationAttribute));
        var result = closed.Invoke(null, [typeof(Ilowercase)]);
        Assert.Equal("Ilowercase", result);
    }

    #endregion

    #region RuntimeBridgeService DeserializeParameters HasDefaultValue path

    [Fact]
    public void DeserializeParameters_array_fills_with_default_values_when_available()
    {
        var parameters = typeof(DefaultProbe)
            .GetMethod(nameof(DefaultProbe.WithDefaults))!
            .GetParameters();

        var method = typeof(RuntimeBridgeDynamicFallback).GetMethod(
            "DeserializeParameters",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var jsonArgs = ParseJson("""["first"]""");
        var result = (object?[])method!.Invoke(null, [parameters, (JsonElement?)jsonArgs])!;

        Assert.Equal("first", result[0]);
        Assert.Equal(42, result[1]);
    }

    [Fact]
    public void DeserializeParameters_array_uses_null_for_no_default()
    {
        var parameters = typeof(DefaultProbe)
            .GetMethod(nameof(DefaultProbe.NoDefaults))!
            .GetParameters();

        var method = typeof(RuntimeBridgeDynamicFallback).GetMethod(
            "DeserializeParameters",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var jsonArgs = ParseJson("""["first"]""");
        var result = (object?[])method!.Invoke(null, [parameters, (JsonElement?)jsonArgs])!;

        Assert.Equal("first", result[0]);
        Assert.Null(result[1]);
    }

    #endregion

    #region WebViewCore CompleteActiveNavigation branches

    [Fact]
    public async Task Navigation_failure_with_non_WebViewNavigationException_wraps_error()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        NavigationCompletedEventArgs? completed = null;
        core.NavigationCompleted += (_, e) => completed = e;

        var requestUri = new Uri("https://failure.test/wrap");
        var navTask = core.NavigateAsync(requestUri);
        var navId = adapter.LastNavigationId;

        adapter.RaiseNavigationCompleted(navId!.Value, requestUri, NavigationCompletedStatus.Failure,
            new InvalidOperationException("raw error"));

        var ex = await Assert.ThrowsAsync<WebViewNavigationException>(() => navTask);
        Assert.NotNull(completed);
        Assert.Equal(NavigationCompletedStatus.Failure, completed!.Status);
    }

    [Fact]
    public async Task Navigation_failure_with_WebViewNavigationException_preserved()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var requestUri = new Uri("https://failure.test/preserve");
        var navTask = core.NavigateAsync(requestUri);
        var navId = adapter.LastNavigationId;

        var navEx = new WebViewNavigationException(
            "nav error", navId!.Value, requestUri);
        adapter.RaiseNavigationCompleted(navId!.Value, requestUri, NavigationCompletedStatus.Failure, navEx);

        var ex = await Assert.ThrowsAsync<WebViewNavigationException>(() => navTask);
        Assert.Same(navEx, ex);
    }

    [Fact]
    public async Task Navigation_success_without_subscriber_completes()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var requestUri = new Uri("https://success.test/no-subscriber");
        var navTask = core.NavigateAsync(requestUri);
        var navId = adapter.LastNavigationId;

        adapter.RaiseNavigationCompleted(navId!.Value, requestUri, NavigationCompletedStatus.Success);

        await navTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    #endregion

    #region WebViewCore DownloadRequested and PermissionRequested after dispose

    [Fact]
    public void DownloadRequested_is_ignored_when_disposed()
    {
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, new TestDispatcher());

        DownloadRequestedEventArgs? args = null;
        core.DownloadRequested += (_, e) => args = e;
        core.Dispose();

        adapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(new Uri("https://test/file.zip")));
        Assert.Null(args);
    }

    [Fact]
    public void PermissionRequested_is_ignored_when_disposed()
    {
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, new TestDispatcher());

        PermissionRequestedEventArgs? args = null;
        core.PermissionRequested += (_, e) => args = e;
        core.Dispose();

        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(
            WebViewPermissionKind.Camera, new Uri("https://test")));
        Assert.Null(args);
    }

    #endregion

    #region Test helpers

    private sealed class FakeDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class TrackingProvider : IWebViewHostCapabilityProvider
    {
        public string? ReadClipboardText() => null;
        public void WriteClipboardText(string text) { }
        public WebViewFileDialogResult ShowOpenFileDialog(WebViewOpenFileDialogRequest request) => new() { IsCanceled = true, Paths = [] };
        public WebViewFileDialogResult ShowSaveFileDialog(WebViewSaveFileDialogRequest request) => new() { IsCanceled = true, Paths = [] };
        public void OpenExternal(Uri uri) { }
        public void ShowNotification(WebViewNotificationRequest request) { }
        public void ApplyMenuModel(WebViewMenuModelRequest request) { }
        public void UpdateTrayState(WebViewTrayStateRequest request) { }
        public void ExecuteSystemAction(WebViewSystemActionRequest request) { }
    }

    [JsImport]
    public interface IProbeImport
    {
        Task FireAsync();
        Task<string> GetValueAsync(string key, int count);
    }

    public interface ISyncReturnImport
    {
        string GetValue();
    }

    public interface IStandardInterface { }

    public interface NoIPrefixService { }

    public interface I { }

    public interface Ilowercase { }

    private sealed class DefaultProbe
    {
        public void WithDefaults(string name, int count = 42) => _ = (name, count);
        public void NoDefaults(string name, int count) => _ = (name, count);
    }

    private sealed class TestableRpcService : IWebViewRpcService
    {
        public List<string> InvokedMethods { get; } = [];
        private readonly Dictionary<string, Func<JsonElement?, Task<object?>>> _handlers = new();

        public void Handle(string method, Func<JsonElement?, Task<object?>> handler) => _handlers[method] = handler;
        public void Handle(string method, Func<JsonElement?, object?> handler) =>
            _handlers[method] = args => Task.FromResult(handler(args));
        public void UnregisterHandler(string method) => _handlers.Remove(method);

        public Task<JsonElement> InvokeAsync(string method, object? args = null)
        {
            InvokedMethods.Add(method);
            return Task.FromResult(default(JsonElement));
        }

        public Task<T?> InvokeAsync<T>(string method, object? args = null)
        {
            InvokedMethods.Add(method);
            return Task.FromResult(default(T));
        }

        public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose) { }
    }

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    #endregion
}
