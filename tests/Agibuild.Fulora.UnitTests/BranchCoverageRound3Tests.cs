using System.Reflection;
using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Shell;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class BranchCoverageRound3Tests
{
    #region Easy: Constructor null checks

    [Fact]
    public void WebDialog_null_host_throws()
    {
        var adapter = new MockWebViewAdapter();
        var dispatcher = new TestDispatcher();
        Assert.Throws<ArgumentNullException>(() => new WebDialog(null!, adapter, dispatcher));
    }

    [Fact]
    public void WebAuthBrokerWithSemantics_null_inner_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WebAuthBrokerWithSemantics(null!));
    }

    [Fact]
    public void RuntimeBridgeService_null_rpc_throws()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeBridgeService(null!, s => Task.FromResult<string?>(null), logger));
    }

    [Fact]
    public void RuntimeBridgeService_null_invokeScript_throws()
    {
        var rpc = new WebViewRpcService(s => Task.FromResult<string?>(null), NullLoggerFactory.Instance.CreateLogger("test"));
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeBridgeService(rpc, null!, logger));
    }

    [Fact]
    public void RuntimeBridgeService_null_logger_throws()
    {
        var rpc = new WebViewRpcService(s => Task.FromResult<string?>(null), NullLoggerFactory.Instance.CreateLogger("test"));
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeBridgeService(rpc, s => Task.FromResult<string?>(null), null!));
    }

    #endregion

    #region Easy: WebViewAdapterRegistry Windows adapter path

    [Fact]
    public void TryCreateForCurrentPlatform_returns_adapter_when_current_platform_registered()
    {
        // Register an adapter for the current OS so this assertion is deterministic in CI matrix runs.
        var reg = new WebViewAdapterRegistration(
            GetCurrentPlatformForTest(), "branch-coverage-test-adapter",
            () => new MockWebViewAdapter(), Priority: int.MaxValue);
        WebViewAdapterRegistry.Register(reg);

        var result = WebViewAdapterRegistry.TryCreateForCurrentPlatform(out var adapter, out var reason);
        Assert.True(result);
        Assert.NotNull(adapter);
        Assert.Null(reason);
    }

    #endregion

    private static WebViewAdapterPlatform GetCurrentPlatformForTest()
    {
        if (OperatingSystem.IsWindows())
        {
            return WebViewAdapterPlatform.Windows;
        }

        if (OperatingSystem.IsIOS())
        {
            return WebViewAdapterPlatform.iOS;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WebViewAdapterPlatform.MacOS;
        }

        if (OperatingSystem.IsAndroid())
        {
            return WebViewAdapterPlatform.Android;
        }

        return WebViewAdapterPlatform.Gtk;
    }

    #region Easy: ActivationRequest explicit receivedAtUtc

    [Fact]
    public void ActivationRequest_explicit_receivedAtUtc_covers_non_null_path()
    {
        var ts = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var request = new WebViewShellActivationRequest(
            new Uri("https://example.test/activate"), receivedAtUtc: ts);
        Assert.Equal(ts, request.ReceivedAtUtc);
    }

    #endregion

    #region Easy: NormalizeProfileHash uppercase hex

    [Fact]
    public void NormalizeProfileHash_uppercase_hex_covers_branch()
    {
        // Using uppercase hex to cover the `isUpperHex` true branch (line 128)
        // and the `isDecimal` false branch (line 126)
        var method = typeof(WebViewSessionPermissionProfile).GetMethod(
            "NormalizeProfileHash", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var upperHash = "sha256:" + "AABBCCDD" + new string('E', 56);
        var result = method!.Invoke(null, [upperHash]);
        Assert.NotNull(result);
        Assert.StartsWith("sha256:", (string)result!);
    }

    [Fact]
    public void NormalizeProfileHash_mixed_case_hex_covers_all_char_branches()
    {
        var method = typeof(WebViewSessionPermissionProfile).GetMethod(
            "NormalizeProfileHash", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        // Mix of decimal, lowercase, and uppercase to cover all three branches
        var mixedHash = "sha256:" + "0123456789abcdefABCDEF" + new string('0', 42);
        var result = method!.Invoke(null, [mixedHash]);
        Assert.NotNull(result);
    }

    #endregion

    #region Medium: WebViewCore adapterDestroyed paths

    [Fact]
    public void Events_ignored_when_adapterDestroyed_but_not_disposed()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateFull();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        var events = new List<string>();
        core.NavigationCompleted += (_, _) => events.Add("NavigationCompleted");
        core.NewWindowRequested += (_, _) => events.Add("NewWindowRequested");
        core.WebMessageReceived += (_, _) => events.Add("WebMessageReceived");
        core.WebResourceRequested += (_, _) => events.Add("WebResourceRequested");
        core.EnvironmentRequested += (_, _) => events.Add("EnvironmentRequested");
        core.DownloadRequested += (_, _) => events.Add("DownloadRequested");
        core.PermissionRequested += (_, _) => events.Add("PermissionRequested");

        // Set _adapterDestroyed=true via reflection (without Dispose which also sets _disposed)
        var field = typeof(WebViewCore).GetField("_adapterDestroyed", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(core, true);

        // Fire all event types — each should hit the _adapterDestroyed path
        adapter.RaiseNavigationCompleted(NavigationCompletedStatus.Success);
        adapter.RaiseNewWindowRequested(new Uri("https://test.example"));
        adapter.RaiseWebMessage("{}", "https://test.example", Guid.NewGuid());
        adapter.RaiseWebResourceRequested();
        adapter.RaiseEnvironmentRequested();
        adapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(
            new Uri("https://dl.example/file.zip"), "file.zip", "application/zip"));
        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(
            WebViewPermissionKind.Camera, new Uri("https://test.example")));

        Assert.Empty(events);
    }

    #endregion

    #region Medium: WebViewCore InvokeAsyncOnUiThread disposed

    [Fact]
    public async Task InvokeAsyncOnUiThread_disposed_returns_failure()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCommands();
        var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        core.Dispose();

        var ex = await Assert.ThrowsAsync<ObjectDisposedException>(() => core.NavigateAsync(new Uri("https://test.example")));
        Assert.NotNull(ex);
    }

    #endregion

    #region Medium: WebViewShellExperience null-coalescing branches

    [Fact]
    public void ApplyMenuModel_null_effectiveMenuModel_uses_normalizedRequest()
    {
        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);
        using var webView = CreateFullWebView();
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
        });

        var request = new WebViewMenuModelRequest();
        var result = shell.ApplyMenuModel(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryApplyProfilePermission_sessionDecision_null_ScopeIdentity()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithPermission();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        // No SessionPolicy set → _sessionDecision is null
        // SessionPermissionProfileResolver is set → TryApplyProfilePermissionDecision runs
        // Line 1097: _sessionDecision?.ScopeIdentity evaluates to null, falls through to ?? path
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((ctx, _) =>
                new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = "test-profile"
                })
        });

        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(WebViewPermissionKind.Camera));
    }

    [Fact]
    public void ExternalBrowser_deny_with_null_reason_covers_coalesce()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, policy: new NullReasonDenyPolicy());
        WebViewShellPolicyErrorEventArgs? error = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ExternalBrowser()),
            PolicyErrorHandler = (_, e) => error = e
        });

        adapter.RaiseNewWindowRequested(new Uri("https://external.test"));
        DispatcherTestPump.WaitUntil(dispatcher, () => error is not null);

        Assert.Equal(WebViewShellPolicyDomain.ExternalOpen, error!.Domain);
        Assert.Contains("External open was denied by host capability policy.", error.Exception.Message);
    }

    [Fact]
    public void ReportSystemIntegrationOutcome_deny_null_reason_covers_coalesce()
    {
        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, policy: new NullReasonDenyPolicy());
        using var webView = CreateFullWebView();
        var policyErrors = new List<WebViewShellPolicyErrorEventArgs>();
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge
        });
        shell.PolicyError += (_, e) => policyErrors.Add(e);

        var result = shell.ReadClipboardText();
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, result.Outcome);
    }

    [Fact]
    public void Dispose_with_managed_window_in_closing_state()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ManagedWindow()),
            ManagedWindowFactory = _ => CreateFullWebView()
        });

        adapter.RaiseNewWindowRequested(new Uri("https://child.test"));
        DispatcherTestPump.WaitUntil(dispatcher, () => shell.ManagedWindowCount == 1);

        // Set the managed window's state to Closing via reflection
        var managedWindowsField = typeof(WebViewShellExperience).GetField(
            "_managedWindows", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(managedWindowsField);
        var windows = managedWindowsField!.GetValue(shell);
        var entriesProperty = windows!.GetType().GetProperty("Values");
        var entries = entriesProperty!.GetValue(windows) as System.Collections.IEnumerable;
        foreach (var entry in entries!)
        {
            var stateProperty = entry.GetType().GetProperty("State");
            stateProperty!.SetValue(entry, WebViewManagedWindowLifecycleState.Closing);
        }

        shell.Dispose();
    }

    #endregion

    #region Medium: WebViewRpcService notification handlers

    [Fact]
    public void HandleNotification_cancelRequest_cancels_active_cts()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // Register a pending cancellation via reflection
        var cancellationsField = typeof(WebViewRpcService).GetField(
            "_activeCancellations", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(cancellationsField);
        var cancellations = cancellationsField!.GetValue(rpc) as System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>;
        Assert.NotNull(cancellations);

        var cts = new CancellationTokenSource();
        cancellations!["test-cancel-id"] = cts;

        // Send $/cancelRequest notification
        var cancelJson = """{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":"test-cancel-id"}}""";
        var handled = rpc.TryProcessMessage(cancelJson);
        Assert.True(handled);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void HandleNotification_cancelRequest_unknown_id_still_returns_true()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // Line 453: targetId is not null but key not found in _activeCancellations
        var cancelJson = """{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":"nonexistent"}}""";
        var handled = rpc.TryProcessMessage(cancelJson);
        Assert.True(handled);
    }

    [Fact]
    public void HandleNotification_cancelRequest_null_id_returns_true()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // Line 453: targetId is null → `targetId is not null` fails → skip cancel
        var cancelJson = """{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":null}}""";
        var handled = rpc.TryProcessMessage(cancelJson);
        Assert.True(handled);
    }

    [Fact]
    public void HandleNotification_enumeratorAbort_dispatches()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // Line 459: $/enumerator/abort notification
        var abortJson = """{"jsonrpc":"2.0","method":"$/enumerator/abort","params":{"token":"test-token"}}""";
        var handled = rpc.TryProcessMessage(abortJson);
        Assert.True(handled);
    }

    [Fact]
    public void ResolvePendingCall_error_without_message_uses_default()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // Register a pending call via reflection
        var pendingField = typeof(WebViewRpcService).GetField(
            "_pendingCalls", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(pendingField);
        var pending = pendingField!.GetValue(rpc) as System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>;
        Assert.NotNull(pending);

        var tcs = new TaskCompletionSource<JsonElement>();
        pending!["error-test-id"] = tcs;

        // Send response with error that has code but no message → line 478 covers `m.GetString() ?? "RPC error"`
        var errorJson = """{"jsonrpc":"2.0","id":"error-test-id","error":{"code":-32600}}""";
        rpc.TryProcessMessage(errorJson);

        Assert.True(tcs.Task.IsFaulted);
        var rpcEx = Assert.IsType<WebViewRpcException>(tcs.Task.Exception!.InnerException);
        Assert.Equal(-32600, rpcEx.Code);
    }

    [Fact]
    public void ResolvePendingCall_error_with_null_message_uses_default()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        var pendingField = typeof(WebViewRpcService).GetField(
            "_pendingCalls", BindingFlags.NonPublic | BindingFlags.Instance);
        var pending = pendingField!.GetValue(rpc) as System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>;

        var tcs = new TaskCompletionSource<JsonElement>();
        pending!["null-msg-id"] = tcs;

        // Error with explicit null message → GetString() returns null → ?? "RPC error"
        var errorJson = """{"jsonrpc":"2.0","id":"null-msg-id","error":{"code":-32600,"message":null}}""";
        rpc.TryProcessMessage(errorJson);

        Assert.True(tcs.Task.IsFaulted);
        var rpcEx = Assert.IsType<WebViewRpcException>(tcs.Task.Exception!.InnerException);
        Assert.Equal("RPC error", rpcEx.Message);
    }

    #endregion

    #region Medium: WebViewShellExperience IsTransitionAllowed / TryTransition failure paths

    [Fact]
    public void IsTransitionAllowed_invalid_transition_returns_false()
    {
        // Use reflection to test the static method
        var method = typeof(WebViewShellExperience).GetMethod(
            "IsTransitionAllowed", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        // null → Attached (invalid: null can only go to Created)
        var result1 = (bool)method!.Invoke(null, [null, WebViewManagedWindowLifecycleState.Attached])!;
        Assert.False(result1);

        // Created → Ready (invalid: Created can only go to Attached or Closing)
        var result2 = (bool)method!.Invoke(null,
            [WebViewManagedWindowLifecycleState.Created, WebViewManagedWindowLifecycleState.Ready])!;
        Assert.False(result2);

        // Ready → Created (invalid: Ready can only go to Closing)
        var result3 = (bool)method!.Invoke(null,
            [WebViewManagedWindowLifecycleState.Ready, WebViewManagedWindowLifecycleState.Created])!;
        Assert.False(result3);

        // Closed → Created (invalid: Closed is terminal)
        var result4 = (bool)method!.Invoke(null,
            [WebViewManagedWindowLifecycleState.Closed, WebViewManagedWindowLifecycleState.Created])!;
        Assert.False(result4);

        // Created → Closing (valid)
        var result5 = (bool)method!.Invoke(null,
            [WebViewManagedWindowLifecycleState.Created, WebViewManagedWindowLifecycleState.Closing])!;
        Assert.True(result5);

        // Attached → Closing (valid)
        var result6 = (bool)method!.Invoke(null,
            [WebViewManagedWindowLifecycleState.Attached, WebViewManagedWindowLifecycleState.Closing])!;
        Assert.True(result6);

        // Closing → Closed (valid)
        var result7 = (bool)method!.Invoke(null,
            [WebViewManagedWindowLifecycleState.Closing, WebViewManagedWindowLifecycleState.Closed])!;
        Assert.True(result7);
    }

    #endregion

    #region Medium: WebViewCore WebMessageReceived with rpc null + no subscriber

    [Fact]
    public void WebMessageReceived_allowed_without_subscriber_does_not_throw()
    {
        // Line 1350: WebMessageReceived?.Invoke — null-conditional with no subscriber
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string>(StringComparer.Ordinal) { "https://test.example" }
        });

        // Send a web message that is allowed by policy but has no subscriber
        // This covers the null-conditional ?.Invoke path
        adapter.RaiseWebMessage(
            """{"data":"test"}""",
            "https://test.example",
            core.ChannelId,
            protocolVersion: 1);
    }

    #endregion

    #region Medium: WebViewCore RedoAsync

    [Fact]
    public async Task RedoAsync_executes_via_command_manager()
    {
        // Line 1604: RedoAsync coverage
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCommands();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        var cmdMgr = core.TryGetCommandManager();
        Assert.NotNull(cmdMgr);

        await cmdMgr!.RedoAsync();
        Assert.Contains(WebViewCommand.Redo, ((MockWebViewAdapterWithCommands)adapter).ExecutedCommands);
    }

    #endregion

    #region Medium: RuntimeBridgeService ExtractMethodName no dot

    [Fact]
    public void ExtractMethodName_no_dot_returns_full_string()
    {
        // ExtractMethodName is now in RpcMethodHelpers (internal, accessible via InternalsVisibleTo)
        var result = RpcMethodHelpers.ExtractMethodName("NoDotMethodName");
        Assert.Equal("NoDotMethodName", result);

        var result2 = RpcMethodHelpers.ExtractMethodName("Service.Method");
        Assert.Equal("Method", result2);
    }

    #endregion

    #region Medium: WebViewCore EnableWebMessageBridge with null AllowedOrigins

    [Fact]
    public void EnableWebMessageBridge_null_origins_covers_coalesce()
    {
        // Line 900: options.AllowedOrigins?.Count ?? 0
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        var options = new WebMessageBridgeOptions { AllowedOrigins = null! };
        core.EnableWebMessageBridge(options);
    }

    #endregion

    #region Round 4: WebViewCore _disposed=true via reflection for event handlers

    [Fact]
    public void Events_ignored_when_disposed_but_not_detached_from_adapter()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateFull();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        var events = new List<string>();
        core.NavigationCompleted += (_, _) => events.Add("NavigationCompleted");
        core.NewWindowRequested += (_, _) => events.Add("NewWindowRequested");
        core.WebMessageReceived += (_, _) => events.Add("WebMessageReceived");
        core.WebResourceRequested += (_, _) => events.Add("WebResourceRequested");
        core.EnvironmentRequested += (_, _) => events.Add("EnvironmentRequested");
        core.DownloadRequested += (_, _) => events.Add("DownloadRequested");
        core.PermissionRequested += (_, _) => events.Add("PermissionRequested");

        // Set _disposed=true via reflection without calling Dispose (which would unsubscribe adapter events)
        var field = typeof(WebViewCore).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(core, true);

        // Raise all events — each should be silently ignored due to _disposed=true
        adapter.RaiseNavigationCompleted(NavigationCompletedStatus.Success);
        adapter.RaiseNewWindowRequested(new Uri("https://test.example"));
        adapter.RaiseWebMessage("{}", "https://test.example", Guid.NewGuid());
        adapter.RaiseWebResourceRequested();
        adapter.RaiseEnvironmentRequested();
        adapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(
            new Uri("https://dl.example/file.zip"), "file.zip", "application/zip"));
        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(
            WebViewPermissionKind.Camera, new Uri("https://test.example")));

        Assert.Empty(events);
    }

    #endregion

    #region Round 4: WebResourceRequested/EnvironmentRequested with subscriber

    [Fact]
    public void WebResourceRequested_with_subscriber_invokes_handler()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        var invoked = false;
        core.WebResourceRequested += (_, _) => invoked = true;
        adapter.RaiseWebResourceRequested();
        Assert.True(invoked);
    }

    [Fact]
    public void EnvironmentRequested_with_subscriber_invokes_handler()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        var invoked = false;
        core.EnvironmentRequested += (_, _) => invoked = true;
        adapter.RaiseEnvironmentRequested();
        Assert.True(invoked);
    }

    [Fact]
    public void WebResourceRequested_without_subscriber_does_not_throw()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        // No subscriber attached — covers the null-delegate branch of ?.Invoke
        adapter.RaiseWebResourceRequested();
    }

    #endregion

    #region Round 4: RPC notification edge cases

    [Fact]
    public void HandleNotification_unknown_method_returns_false()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // A notification with an unknown method should reach line 459 with methodName != "$/enumerator/abort"
        var json = """{"jsonrpc":"2.0","method":"$/some/unknown/method","params":{}}""";
        var handled = rpc.TryProcessMessage(json);
        Assert.False(handled);
    }

    [Fact]
    public void HandleNotification_enumeratorAbort_without_params_falls_through()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // $/enumerator/abort without "params" → TryGetProperty("params",...) returns false → falls through
        var json = """{"jsonrpc":"2.0","method":"$/enumerator/abort"}""";
        var handled = rpc.TryProcessMessage(json);
        Assert.False(handled);
    }

    #endregion

    #region Round 4: NormalizeProfileHash char < '0'

    [Fact]
    public void NormalizeProfileHash_char_below_zero_covers_false_branch()
    {
        // Line 126: c is >= '0' → false branch (never tested because all hex chars are >= '0')
        // Use a char that is < '0' in ASCII (e.g., '/' = 0x2F, space = 0x20)
        var method = typeof(WebViewSessionPermissionProfile).GetMethod(
            "NormalizeProfileHash", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        // Hash with '/' character (ASCII 47, < '0' which is ASCII 48)
        var invalidHash = "sha256:" + "/" + new string('0', 63);
        var result = (string?)method!.Invoke(null, [invalidHash]);
        Assert.Null(result);
    }

    #endregion

    #region Round 4: SPA autoInject when bridge already enabled

    [Fact]
    public void EnableSpaHosting_autoInject_skipped_when_bridge_already_enabled()
    {
        // Line 944: options.AutoInjectBridgeScript && !_webMessageBridgeEnabled
        // Cover the branch where AutoInjectBridgeScript=true but _webMessageBridgeEnabled=true
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateFull();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        core.EnableWebMessageBridge(new WebMessageBridgeOptions());

        core.EnableSpaHosting(new SpaHostingOptions
        {
            DevServerUrl = "http://localhost:5173"
        });
    }

    [Fact]
    public void EnableSpaHosting_autoInject_disabled_covers_false_branch()
    {
        // Line 944: AutoInjectBridgeScript=false → short-circuit, skip auto-inject
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateFull();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        core.EnableSpaHosting(new SpaHostingOptions
        {
            DevServerUrl = "http://localhost:5173",
            AutoInjectBridgeScript = false
        });
    }

    #endregion

    #region Round 4: Menu pruning denied with null reason

    [Fact]
    public void MenuPruning_deny_with_null_reason_covers_coalesce()
    {
        // Line 1604: decision.DenyReason ?? "menu-pruning-policy-denied"
        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);
        using var webView = CreateFullWebView();
        var policyErrors = new List<WebViewShellPolicyErrorEventArgs>();
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            MenuPruningPolicy = new DelegateMenuPruningPolicy((_, _) =>
                new WebViewMenuPruningDecision(IsAllowed: false, DenyReason: null))
        });
        shell.PolicyError += (_, e) => policyErrors.Add(e);

        var result = shell.ApplyMenuModel(new WebViewMenuModelRequest());
        Assert.NotNull(result);
        Assert.True(policyErrors.Count > 0);
    }

    #endregion

    #region Round 4: Menu pruning profile scope with null _sessionDecision

    [Fact]
    public void MenuPruning_profile_scope_uses_session_context_when_sessionDecision_null()
    {
        // Line 1621: _sessionDecision?.ScopeIdentity ?? _options.SessionContext.ScopeIdentity
        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);
        using var webView = CreateFullWebView();
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            MenuPruningPolicy = new DelegateMenuPruningPolicy((_, _) =>
                new WebViewMenuPruningDecision(IsAllowed: true)),
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((ctx, _) =>
                new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = "test-profile"
                })
        });

        var result = shell.ApplyMenuModel(new WebViewMenuModelRequest());
        Assert.NotNull(result);
    }

    #endregion

    #region Round 4: Navigate to about:blank

    [Fact]
    public async Task NavigationStarting_with_about_blank_uri_covers_ternary()
    {
        // Line 541: info.RequestUri.AbsoluteUri != AboutBlank.AbsoluteUri ? info.RequestUri : AboutBlank
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter { AutoCompleteNavigation = true };
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        await core.NavigateAsync(new Uri("about:blank"));
    }

    #endregion

    #region Round 5 Tier 1: WebViewCore remaining branches

    [Fact]
    public void EnvironmentRequested_without_subscriber_does_not_throw()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        adapter.RaiseEnvironmentRequested();
    }

    [Fact]
    public void WebMessageReceived_non_rpc_body_falls_through_to_event()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        core.EnableWebMessageBridge(new WebMessageBridgeOptions());
        var received = false;
        core.WebMessageReceived += (_, _) => received = true;

        adapter.RaiseWebMessage(
            body: """{"type":"not-rpc"}""",
            origin: "",
            channelId: core.ChannelId,
            protocolVersion: 1);

        Assert.True(received);
    }

    [Fact]
    public void ThrowIfNotOnUiThread_from_background_thread_throws()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        InvalidOperationException? caught = null;
        var thread = new Thread(() =>
        {
            try { core.EnableWebMessageBridge(new WebMessageBridgeOptions()); }
            catch (InvalidOperationException ex) { caught = ex; }
        });
        thread.Start();
        thread.Join();

        Assert.NotNull(caught);
        Assert.Contains("must be called on the UI thread", caught!.Message);
    }

    #endregion

    #region Round 5 Tier 1: RuntimeBridgeService proxy branches

    [Fact]
    public void BridgeImportProxy_no_args_method_sends_null_params()
    {
        var proxy = DispatchProxy.Create<INoArgImport, BridgeImportProxy>();
        var bridgeProxy = (BridgeImportProxy)(object)proxy;

        string? capturedMethod = null;
        object? capturedParams = null;
        var mockRpc = new LambdaRpcService((method, p) =>
        {
            capturedMethod = method;
            capturedParams = p;
            return Task.CompletedTask;
        });
        bridgeProxy.Initialize(mockRpc, "TestSvc");

        _ = proxy.DoAsync();

        Assert.Equal("TestSvc.doAsync", capturedMethod);
        Assert.Null(capturedParams);
    }

    [Fact]
    public void BridgeImportProxy_non_task_return_throws_not_supported()
    {
        var proxy = DispatchProxy.Create<ISyncReturnImport, BridgeImportProxy>();
        var bridgeProxy = (BridgeImportProxy)(object)proxy;

        var mockRpc = new LambdaRpcService((_, _) => Task.CompletedTask);
        bridgeProxy.Initialize(mockRpc, "TestSvc");

        Assert.Throws<NotSupportedException>(() => proxy.GetValue());
    }

    #endregion

    #region Round 5 Tier 1: SpaAssetHotUpdateService NormalizeVersion

    [Fact]
    public void NormalizeVersion_whitespace_only_throws()
    {
        var method = typeof(SpaAssetHotUpdateService)
            .GetMethod("NormalizeVersion", BindingFlags.NonPublic | BindingFlags.Static)!;

        var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, ["   "]));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    #endregion

    #region Round 5 Tier 1: WebViewShellExperience managed window lifecycle

    [Fact]
    public void IsTransitionAllowed_created_to_closing_returns_true()
    {
        var method = typeof(WebViewShellExperience)
            .GetMethod("IsTransitionAllowed", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (bool)method.Invoke(null, [
            WebViewManagedWindowLifecycleState.Created,
            WebViewManagedWindowLifecycleState.Closing
        ])!;

        Assert.True(result);
    }

    [Fact]
    public void IsTransitionAllowed_attached_to_closing_returns_true()
    {
        var method = typeof(WebViewShellExperience)
            .GetMethod("IsTransitionAllowed", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (bool)method.Invoke(null, [
            WebViewManagedWindowLifecycleState.Attached,
            WebViewManagedWindowLifecycleState.Closing
        ])!;

        Assert.True(result);
    }

    [Fact]
    public void IsTransitionAllowed_closed_to_created_returns_false()
    {
        var method = typeof(WebViewShellExperience)
            .GetMethod("IsTransitionAllowed", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (bool)method.Invoke(null, [
            WebViewManagedWindowLifecycleState.Closed,
            WebViewManagedWindowLifecycleState.Created
        ])!;

        Assert.False(result);
    }

    #endregion

    #region Round 5 Tier 2: WebViewCore OnNativeNavigationStarting about:blank ternary

    [Fact]
    public async Task NativeNavigationStarting_about_blank_covers_ternary()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        var decision = await adapter.SimulateNativeNavigationStartingAsync(new Uri("about:blank"));
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task NativeNavigationStarting_normal_uri_covers_ternary()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapter();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        var decision = await adapter.SimulateNativeNavigationStartingAsync(new Uri("https://example.com"));
        Assert.True(decision.IsAllowed);
    }

    #endregion

    #region Round 5 Tier 2: BridgeImportProxy.Invoke with null args

    [Fact]
    public void BridgeImportProxy_invoke_with_null_args_sends_null_params()
    {
        var proxy = DispatchProxy.Create<INoArgImport, BridgeImportProxy>();
        var bridgeProxy = (BridgeImportProxy)(object)proxy;

        string? capturedMethod = null;
        object? capturedParams = null;
        var mockRpc = new LambdaRpcService((method, p) =>
        {
            capturedMethod = method;
            capturedParams = p;
            return Task.FromResult(default(JsonElement));
        });
        bridgeProxy.Initialize(mockRpc, "TestSvc");

        var invokeMethod = typeof(BridgeImportProxy)
            .GetMethod("Invoke", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var targetMethod = typeof(INoArgImport).GetMethod("DoAsync")!;

        invokeMethod.Invoke(bridgeProxy, [targetMethod, null]);

        Assert.Equal("TestSvc.doAsync", capturedMethod);
        Assert.Null(capturedParams);
    }

    #endregion

    #region Round 5 Tier 2: Menu pruning EffectiveMenuModel not null

    [Fact]
    public void MenuPruning_allow_with_effective_menu_covers_non_null_branch()
    {
        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);
        using var webView = CreateFullWebView();

        var effectiveMenu = new WebViewMenuModelRequest();
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            MenuPruningPolicy = new DelegateMenuPruningPolicy((_, _) =>
                new WebViewMenuPruningDecision(IsAllowed: true, EffectiveMenuModel: effectiveMenu))
        });

        var result = shell.ApplyMenuModel(new WebViewMenuModelRequest());
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, result.Outcome);
    }

    #endregion

    #region Round 5 Tier 2: SessionDecision non-null covers ?. path

    [Fact]
    public void MenuPruning_profile_scope_uses_session_decision_when_not_null()
    {
        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);
        using var webView = CreateFullWebView();
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            MenuPruningPolicy = new DelegateMenuPruningPolicy((_, _) =>
                new WebViewMenuPruningDecision(IsAllowed: true)),
            SessionPolicy = new IsolatedSessionPolicy(),
            SessionContext = new WebViewShellSessionContext("test-scope"),
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((ctx, _) =>
                new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = "test-profile"
                })
        });

        var result = shell.ApplyMenuModel(new WebViewMenuModelRequest());
        Assert.NotNull(result);
        Assert.NotNull(shell.SessionDecision);
    }

    [Fact]
    public void PermissionRequested_with_session_decision_covers_non_null_path()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateFull();
        using var core = new WebViewCore(adapter, dispatcher);
        core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            PermissionPolicy = new DelegatePermissionPolicy((_, e) => e.State = PermissionState.Allow),
            SessionPolicy = new IsolatedSessionPolicy(),
            SessionContext = new WebViewShellSessionContext("test-scope"),
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((ctx, _) =>
                new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = "perm-profile"
                })
        });

        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.com")));
        Assert.NotNull(shell.SessionDecision);
    }

    #endregion

    #region Round 5 Tier 2: WebViewHostCapabilityBridge metadata validation

    [Fact]
    public void HostCapabilityBridge_metadata_too_many_entries_denied()
    {
        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < 10; i++)
            metadata[$"platform.extension.key{i}"] = $"value{i}";

        var request = new WebViewSystemIntegrationEventRequest
        {
            Source = "test",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "item1",
            Metadata = metadata
        };

        var result = bridge.DispatchSystemIntegrationEvent(request, Guid.NewGuid());
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, result.Outcome);
    }

    [Fact]
    public void HostCapabilityBridge_metadata_value_too_long_denied()
    {
        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);

        var request = new WebViewSystemIntegrationEventRequest
        {
            Source = "test",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "item1",
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["platform.extension.data"] = new string('x', 300)
            }
        };

        var result = bridge.DispatchSystemIntegrationEvent(request, Guid.NewGuid());
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, result.Outcome);
    }

    #endregion

    #region Round 5 Tier 2: WebViewShellExperience ReportSystemIntegrationOutcome

    [Fact]
    public void UpdateTrayState_deny_with_null_reason_covers_coalesce()
    {
        var provider = new MinimalHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new NullReasonDenyPolicy());
        using var webView = CreateFullWebView();
        WebViewShellPolicyErrorEventArgs? error = null;
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            PolicyErrorHandler = (_, e) => error = e
        });

        var result = shell.UpdateTrayState(new WebViewTrayStateRequest { IsVisible = true });

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, result.Outcome);
        Assert.NotNull(error);
    }

    #endregion

    #region Helpers

    private static FullWebView CreateFullWebView() => new();

    private sealed class FullWebView : IWebView
    {
        public Uri Source { get; set; } = new("about:blank");
        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public bool IsLoading => false;
        public Guid ChannelId { get; } = Guid.NewGuid();
        public ICommandManager? CommandManager { get; init; }
        public bool IsDisposed { get; private set; }
        private bool _isDevToolsOpen;

        public event EventHandler<NavigationStartingEventArgs>? NavigationStarted { add { } remove { } }
        public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted { add { } remove { } }
        public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested { add { } remove { } }
        public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived { add { } remove { } }
        public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested { add { } remove { } }
        public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested { add { } remove { } }
        public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested { add { } remove { } }
        public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested { add { } remove { } }
        public event EventHandler<AdapterCreatedEventArgs>? AdapterCreated { add { } remove { } }
        public event EventHandler? AdapterDestroyed { add { } remove { } }
        public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested { add { } remove { } }

        public Task NavigateAsync(Uri uri) => Task.CompletedTask;
        public Task NavigateToStringAsync(string html) => Task.CompletedTask;
        public Task NavigateToStringAsync(string html, Uri? baseUrl) => Task.CompletedTask;
        public Task<string?> InvokeScriptAsync(string script) => Task.FromResult<string?>(null);
        public Task<bool> GoBackAsync() => Task.FromResult(false);
        public Task<bool> GoForwardAsync() => Task.FromResult(false);
        public Task<bool> RefreshAsync() => Task.FromResult(false);
        public Task<bool> StopAsync() => Task.FromResult(false);
        public ICookieManager? TryGetCookieManager() => null;
        public ICommandManager? TryGetCommandManager() => CommandManager;
        public Task<INativeHandle?> TryGetWebViewHandleAsync() => Task.FromResult<INativeHandle?>(null);
        public IWebViewRpcService? Rpc => null;
        public IBridgeService Bridge => throw new NotSupportedException();
        public IBridgeTracer? BridgeTracer { get; set; }
        public Task<byte[]> CaptureScreenshotAsync() => Task.FromException<byte[]>(new NotSupportedException());
        public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => Task.FromException<byte[]>(new NotSupportedException());
        public Task<double> GetZoomFactorAsync() => Task.FromResult(1.0);
        public Task SetZoomFactorAsync(double zoomFactor) => Task.CompletedTask;
        public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null) => Task.FromException<FindInPageEventArgs>(new NotSupportedException());
        public Task StopFindInPageAsync(bool clearHighlights = true) => Task.CompletedTask;
        public Task<string> AddPreloadScriptAsync(string javaScript) => Task.FromException<string>(new NotSupportedException());
        public Task RemovePreloadScriptAsync(string scriptId) => Task.FromException(new NotSupportedException());
        public Task OpenDevToolsAsync() { _isDevToolsOpen = true; return Task.CompletedTask; }
        public Task CloseDevToolsAsync() { _isDevToolsOpen = false; return Task.CompletedTask; }
        public Task<bool> IsDevToolsOpenAsync() => Task.FromResult(_isDevToolsOpen);

        public void Dispose()
        {
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private sealed class MinimalHostCapabilityProvider : IWebViewHostCapabilityProvider
    {
        public string? ReadClipboardText() => null;
        public void WriteClipboardText(string text) { }
        public WebViewFileDialogResult ShowOpenFileDialog(WebViewOpenFileDialogRequest request) => new() { IsCanceled = true };
        public WebViewFileDialogResult ShowSaveFileDialog(WebViewSaveFileDialogRequest request) => new() { IsCanceled = true };
        public void OpenExternal(Uri uri) { }
        public void ShowNotification(WebViewNotificationRequest request) { }
        public void ApplyMenuModel(WebViewMenuModelRequest request) { }
        public void UpdateTrayState(WebViewTrayStateRequest request) { }
        public void ExecuteSystemAction(WebViewSystemActionRequest request) { }
    }

    private sealed class NullReasonDenyPolicy : IWebViewHostCapabilityPolicy
    {
        public WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context)
            => WebViewHostCapabilityDecision.Deny(null);
    }

    [JsImport]
    public interface INoArgImport
    {
        Task DoAsync();
    }

    public interface ISyncReturnImport
    {
        string GetValue();
    }

    private sealed class LambdaRpcService : IWebViewRpcService
    {
        private readonly Func<string, object?, Task> _invoker;

        public LambdaRpcService(Func<string, object?, Task> invoker) => _invoker = invoker;

        public void Handle(string method, Func<JsonElement?, Task<object?>> handler) { }
        public void Handle(string method, Func<JsonElement?, object?> handler) { }
        public void UnregisterHandler(string method) { }
        public Task<JsonElement> InvokeAsync(string method, object? args = null)
        {
            _invoker(method, args);
            return Task.FromResult(default(JsonElement));
        }
        public Task<T?> InvokeAsync<T>(string method, object? args = null) => Task.FromResult<T?>(default);
        public bool TryProcessMessage(string body) => false;
        public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose) { }
    }

    #endregion
}
