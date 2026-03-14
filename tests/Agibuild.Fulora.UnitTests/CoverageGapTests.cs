using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Supplementary tests targeting uncovered code paths in record structs,
/// WebViewCore edge cases, and WebDialog error paths.
/// </summary>
public sealed partial class CoverageGapTests
{
    private readonly TestDispatcher _dispatcher = new();

    // ========================= WebMessageEnvelope =========================

    // ========================= WebViewCookie =========================

    // ========================= WebMessagePolicyDecision =========================

    // ========================= NativeNavigationStartingInfo =========================

    // ========================= WebViewAdapterRegistration record =========================

    // ========================= WebViewCore — sub-frame auto-allow =========================

    // ========================= WebViewCore — disposed denies native navigation =========================

    // ========================= WebViewCore — same-URL redirect path =========================

    // ========================= WebViewCore — adapter invocation exception =========================

    // ========================= WebViewCore — NavigationCompleted with no active navigation =========================

    // ========================= WebViewCore — Failure without error fills in default =========================

    // ========================= WebViewCore — adapter reports failure with WebViewNavigationException =========================

    // ========================= WebDialog — event remove accessors =========================

    // ========================= WebDialog — DownloadRequested / PermissionRequested event accessors =========================

    // ========================= WebDialog — AdapterDestroyed event =========================

    // ========================= Branch coverage — WebViewCore ctor null checks =========================

    // ========================= Branch coverage — on-thread event dispatch (Download/Permission) =========================

    // Note: WebViewCore line 712 (Failure + null error) is unreachable —
    // NavigationCompletedEventArgs ctor throws if status=Failure and error=null.

    // ========================= Branch coverage — WebMessage on-UI dispatch with RPC not matching =========================

    // ========================= Branch coverage — EnableWebMessageBridge AllowedOrigins null count =========================

    // CompleteActiveNavigation null-operation branch already covered by
    // NavigationCompleted_with_no_active_navigation_is_ignored

    private sealed class TestDropSink : IWebMessageDropDiagnosticsSink
    {
        private readonly List<WebMessageDropDiagnostic> _drops;
        public TestDropSink(List<WebMessageDropDiagnostic> drops) => _drops = drops;
        public void OnMessageDropped(in WebMessageDropDiagnostic diagnostic) => _drops.Add(diagnostic);
    }

    // ========================= Branch coverage — Download/Permission off-thread =========================

    // ========================= Branch coverage — WebMessage off-thread dispatch =========================

    // ========================= Branch coverage — NativeNavigation redirect cancel =========================

    // ========================= WebDialog — new APIs =========================

    // ========================= WebViewCore — NavigationCompleted ID mismatch =========================

    // ========================= WebViewCore — NewWindowRequested unhandled navigates =========================

    // ========================= WebViewCore — NavigationCompleted after dispose =========================

    // ========================= WebViewCore — NewWindowRequested after dispose =========================

    // ========================= WebViewCore — WebResourceRequested after dispose =========================

    // ========================= WebViewCore — EnvironmentRequested after dispose =========================

    // ========================= WebViewCore — WebMessageReceived with bridge not enabled =========================

    // ========================= WebViewCore — WebMessageReceived after dispose =========================

    // ========================= WebViewCore — Command navigation superseding =========================

    // ========================= WebDialog — Source setter =========================

    // ========================= WebViewAdapterRegistry — priority ordering =========================

    // ========================= WebAuthBroker — Show with non-null PlatformHandle =========================

    // ========================= WebViewCore — NavigationCompleted try/catch exception path =========================

    // ========================= Off-thread dispatch paths =========================

    // ========================= Off-thread dispatch + disposed (marshal then dispose) =========================

    // ========================= Helpers =========================

    /// <summary>
    /// Runs an action on a separate thread and blocks until it completes.
    /// After this method returns, we're guaranteed to be back on the original (UI) thread.
    /// </summary>
    private static void RunOnBackgroundThread(Action action)
    {
        Exception? bgException = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { bgException = ex; }
        });
        thread.Start();
        thread.Join();
        if (bgException is not null)
            throw new AggregateException("Background thread threw an exception", bgException);
    }

    private (WebViewCore Core, MockWebViewAdapter Adapter) CreateCoreWithAdapter()
    {
        var adapter = MockWebViewAdapter.Create();
        var core = new WebViewCore(adapter, _dispatcher);
        return (core, adapter);
    }

    /// <summary>Adapter that throws on NavigateAsync to cover the exception catch in StartNavigationCoreAsync.</summary>
#pragma warning disable CS0067 // Events never used (by design — this adapter only throws)
    private sealed class ThrowingNavigateAdapter : IWebViewAdapter
    {
        public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
        public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
        public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
        public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
        public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;

        public void Initialize(IWebViewAdapterHost host) { }
        public void Attach(INativeHandle parentHandle) { }
        public void Detach() { }

        public Task NavigateAsync(Guid navigationId, Uri uri)
            => throw new InvalidOperationException("Simulated adapter failure");

        public Task NavigateToStringAsync(Guid navigationId, string html)
            => throw new InvalidOperationException("Simulated adapter failure");

        public Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl)
            => throw new InvalidOperationException("Simulated adapter failure");

        public Task<string?> InvokeScriptAsync(string script)
            => Task.FromResult<string?>(null);

        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public bool GoBack(Guid navigationId) => false;
        public bool GoForward(Guid navigationId) => false;
        public bool Refresh(Guid navigationId) => false;
        public bool Stop() => false;
    }
#pragma warning restore CS0067

    /// <summary>Window with non-null PlatformHandle for WebAuthBroker test.</summary>
    private sealed class NonNullHandleWindow : ITopLevelWindow
    {
        public INativeHandle? PlatformHandle => new TestPlatformHandle();
    }

    private sealed class TestPlatformHandle : INativeHandle
    {
        public nint Handle => nint.Zero;
        public string HandleDescriptor => "Test";
    }

    /// <summary>Local copy of AuthTestDialogFactory for the coverage gap test.</summary>
    private sealed class AuthTestDialogFactoryLocal : IWebDialogFactory
    {
        private readonly TestDispatcher _dispatcher;

        public AuthTestDialogFactoryLocal(TestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public MockDialogHost? LastHost { get; private set; }
        public Action<WebDialog, MockWebViewAdapter>? OnDialogCreated { get; set; }

        public IWebDialog Create(IWebViewEnvironmentOptions? options = null)
        {
            var host = new MockDialogHost();
            var adapter = MockWebViewAdapter.Create();
            var dialog = new WebDialog(host, adapter, _dispatcher);
            LastHost = host;
            OnDialogCreated?.Invoke(dialog, adapter);
            return dialog;
        }
    }

    // ========================= ICommandManager & ICommandAdapter =========================

    // ========================= IScreenshotAdapter =========================

    // ========================= IPrintAdapter & PdfPrintOptions =========================

    // ========================= IWebViewRpcService =========================

    // ========================= Branch coverage — RPC uncovered branches =========================

    private static void WaitUntil(Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        Assert.True(
            SpinWait.SpinUntil(condition, TimeSpan.FromMilliseconds(timeoutMilliseconds)),
            "Timed out while waiting for asynchronous RPC processing.");
    }

    private static string ExtractRpcId(string script)
    {
        // script looks like: window.agWebView && window.agWebView.rpc && window.agWebView.rpc._dispatch("...escaped json...")
        var start = script.IndexOf("_dispatch(") + "_dispatch(".Length;
        var end = script.LastIndexOf(')');
        var jsonString = script[start..end];
        // jsonString is a JSON-serialized string; deserialize it to get the inner JSON
        var innerJson = JsonSerializer.Deserialize<string>(jsonString)!;
        using var doc = JsonDocument.Parse(innerJson);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static WebViewRpcService CreateTestRpcService(out List<string> capturedScripts)
    {
        var scripts = new List<string>();
        capturedScripts = scripts;
        return new WebViewRpcService(
            script => { scripts.Add(script); return Task.FromResult<string?>(null); },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    // ========================= CamelCase JSON serialization =========================

    // Plain record without [JsonPropertyName] — should serialize as camelCase via BridgeJsonOptions.
    private record PlainProfile(string UserName, bool IsAdmin, int LoginCount);

    // Record with explicit [JsonPropertyName] — attribute should take priority over naming policy.
    private record CustomNameProfile(
        [property: System.Text.Json.Serialization.JsonPropertyName("user_name")] string UserName,
        bool IsAdmin);

    // ==================== FindInPage Tests ====================

    // ==================== Zoom Tests ====================

    // ==================== PreloadScript Tests ====================

    // ==================== ContextMenu Tests ====================

}
