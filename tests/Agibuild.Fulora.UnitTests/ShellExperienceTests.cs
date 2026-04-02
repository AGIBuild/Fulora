using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Agibuild.Fulora.Shell;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class ShellExperienceTests
{
    [Fact]
    public void NavigateInPlace_policy_preserves_v1_fallback_navigation()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new NavigateInPlaceNewWindowPolicy()
        });

        NewWindowRequestedEventArgs? observedArgs = null;
        core.NewWindowRequested += (_, e) => observedArgs = e;

        var uri = new Uri("https://example.com/");
        adapter.RaiseNewWindowRequested(uri);

        DispatcherTestPump.WaitUntil(dispatcher, () => adapter.NavigateCallCount == 1);

        Assert.NotNull(observedArgs);
        Assert.False(observedArgs!.Handled);
        Assert.Equal(uri, adapter.LastNavigationUri);
    }

    [Fact]
    public void Delegate_policy_can_handle_new_window_and_suppress_fallback()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var called = false;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, e) =>
            {
                called = true;
                e.Handled = true;
            })
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/"));

        DispatcherTestPump.WaitUntil(dispatcher, () => called);

        dispatcher.RunAll();
        Assert.Equal(0, adapter.NavigateCallCount);
    }

    [Fact]
    public void Download_handler_can_set_path_and_cancel()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            DownloadHandler = (_, e) =>
            {
                e.DownloadPath = "C:\\temp\\file.bin";
                e.Cancel = true;
            }
        });

        DownloadRequestedEventArgs? observedArgs = null;
        core.DownloadRequested += (_, e) => observedArgs = e;

        adapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(new Uri("https://example.com/file.bin")));

        Assert.NotNull(observedArgs);
        Assert.Equal("C:\\temp\\file.bin", observedArgs!.DownloadPath);
        Assert.True(observedArgs.Cancel);
    }

    [Fact]
    public void Download_policy_runs_before_delegate_handler_in_deterministic_order()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        var order = new List<string>();
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            DownloadPolicy = new DelegateDownloadPolicy((_, e) =>
            {
                order.Add("policy");
                e.DownloadPath = "C:\\policy\\from-policy.bin";
            }),
            DownloadHandler = (_, e) =>
            {
                order.Add("handler");
                e.Cancel = true;
            }
        });

        var args = new DownloadRequestedEventArgs(new Uri("https://example.com/file.bin"));
        adapter.RaiseDownloadRequested(args);

        Assert.Equal(new[] { "policy", "handler" }, order);
        Assert.Equal("C:\\policy\\from-policy.bin", args.DownloadPath);
        Assert.True(args.Cancel);
    }

    [Fact]
    public void Permission_handler_can_allow_or_deny()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            PermissionHandler = (_, e) => e.State = PermissionState.Deny
        });

        PermissionRequestedEventArgs? observedArgs = null;
        core.PermissionRequested += (_, e) => observedArgs = e;

        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.com")));

        Assert.NotNull(observedArgs);
        Assert.Equal(PermissionState.Deny, observedArgs!.State);
    }

    [Fact]
    public void Permission_policy_runs_before_delegate_handler_in_deterministic_order()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        var order = new List<string>();
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            PermissionPolicy = new DelegatePermissionPolicy((_, e) =>
            {
                order.Add("policy");
                e.State = PermissionState.Allow;
            }),
            PermissionHandler = (_, e) =>
            {
                order.Add("handler");
                e.State = PermissionState.Deny;
            }
        });

        var args = new PermissionRequestedEventArgs(WebViewPermissionKind.Camera);
        adapter.RaisePermissionRequested(args);

        Assert.Equal(new[] { "policy", "handler" }, order);
        Assert.Equal(PermissionState.Deny, args.State);
    }

    [Fact]
    public void Shell_experience_with_empty_options_is_non_breaking_for_all_domains()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions());

        var downloadArgs = new DownloadRequestedEventArgs(new Uri("https://example.com/file.bin"));
        var permissionArgs = new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.com"));
        var uri = new Uri("https://example.com/");

        adapter.RaiseDownloadRequested(downloadArgs);
        adapter.RaisePermissionRequested(permissionArgs);
        adapter.RaiseNewWindowRequested(uri);
        DispatcherTestPump.WaitUntil(dispatcher, () => adapter.NavigateCallCount == 1);

        Assert.Equal(uri, adapter.LastNavigationUri);
        Assert.False(downloadArgs.Cancel);
        Assert.Null(downloadArgs.DownloadPath);
        Assert.Equal(PermissionState.Default, permissionArgs.State);
    }

    [Fact]
    public void Policy_failure_isolated_and_reported_without_breaking_other_domains()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        WebViewShellPolicyErrorEventArgs? observedError = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            DownloadPolicy = new DelegateDownloadPolicy((_, _) => throw new InvalidOperationException("download policy failed")),
            PermissionPolicy = new DelegatePermissionPolicy((_, e) => e.State = PermissionState.Deny),
            PolicyErrorHandler = (_, error) => observedError = error
        });

        var downloadArgs = new DownloadRequestedEventArgs(new Uri("https://example.com/file.bin"));
        var permissionArgs = new PermissionRequestedEventArgs(WebViewPermissionKind.Microphone, new Uri("https://example.com"));

        adapter.RaiseDownloadRequested(downloadArgs);
        adapter.RaisePermissionRequested(permissionArgs);

        Assert.NotNull(observedError);
        Assert.Equal(WebViewShellPolicyDomain.Download, observedError!.Domain);
        Assert.True(WebViewOperationFailure.TryGetCategory(observedError.Exception, out var category));
        Assert.Equal(WebViewOperationFailureCategory.AdapterFailed, category);
        Assert.Equal(PermissionState.Deny, permissionArgs.State);
    }

    [Fact]
    public void Session_policy_resolution_is_deterministic_and_propagates_scope_identity()
    {
        var dispatcher = new TestDispatcher();
        var adapter1 = MockWebViewAdapter.Create();
        var adapter2 = MockWebViewAdapter.Create();
        using var core1 = new WebViewCore(adapter1, dispatcher);
        using var core2 = new WebViewCore(adapter2, dispatcher);

        var options = new WebViewShellExperienceOptions
        {
            SessionPolicy = new IsolatedSessionPolicy(),
            SessionContext = new WebViewShellSessionContext("tenant-a")
        };

        using var shell1 = new WebViewShellExperience(core1, options);
        using var shell2 = new WebViewShellExperience(core2, options);

        Assert.NotNull(shell1.SessionDecision);
        Assert.NotNull(shell2.SessionDecision);
        Assert.Equal(shell1.SessionDecision, shell2.SessionDecision);
        Assert.Equal(WebViewShellSessionScope.Isolated, shell1.SessionDecision!.Scope);
        Assert.Equal("isolated:tenant-a", shell1.SessionDecision.ScopeIdentity);
    }

    [Fact]
    public void Session_permission_profile_resolution_is_deterministic_for_equivalent_contexts()
    {
        var dispatcher = new TestDispatcher();
        var adapter1 = MockWebViewAdapter.Create();
        var adapter2 = MockWebViewAdapter.Create();
        using var core1 = new WebViewCore(adapter1, dispatcher);
        using var core2 = new WebViewCore(adapter2, dispatcher);

        var contexts = new List<WebViewSessionPermissionProfileContext>();
        var options = new WebViewShellExperienceOptions
        {
            SessionContext = new WebViewShellSessionContext("tenant-a"),
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((ctx, _) =>
            {
                contexts.Add(ctx);
                return new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = $"profile:{ctx.ScopeIdentity}",
                    SessionDecisionOverride = new WebViewShellSessionDecision(WebViewShellSessionScope.Isolated, $"profile-session:{ctx.ScopeIdentity}")
                };
            })
        };

        using var shell1 = new WebViewShellExperience(core1, options);
        using var shell2 = new WebViewShellExperience(core2, options);

        Assert.NotNull(shell1.SessionDecision);
        Assert.NotNull(shell2.SessionDecision);
        Assert.Equal(shell1.SessionDecision, shell2.SessionDecision);
        Assert.Equal("profile:tenant-a", shell1.RootProfileIdentity);
        Assert.Equal("profile:tenant-a", shell2.RootProfileIdentity);

        Assert.Equal(2, contexts.Count);
        Assert.All(contexts, ctx =>
        {
            Assert.Equal("tenant-a", ctx.ScopeIdentity);
            Assert.Null(ctx.ParentWindowId);
            Assert.Equal(ctx.RootWindowId, ctx.WindowId);
            Assert.Null(ctx.PermissionKind);
        });
    }

    [Fact]
    public void Permission_profile_decision_precedes_fallback_policy_and_handler()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        var order = new List<string>();
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((ctx, _) =>
                new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = "profile-deny-camera",
                    SessionDecisionOverride = new WebViewShellSessionDecision(WebViewShellSessionScope.Shared, "profile"),
                    PermissionDecisions = new Dictionary<WebViewPermissionKind, WebViewPermissionProfileDecision>
                    {
                        [WebViewPermissionKind.Camera] = WebViewPermissionProfileDecision.Deny()
                    }
                }),
            PermissionPolicy = new DelegatePermissionPolicy((_, e) =>
            {
                order.Add("policy");
                e.State = PermissionState.Allow;
            }),
            PermissionHandler = (_, e) =>
            {
                order.Add("handler");
                e.State = PermissionState.Allow;
            }
        });

        var args = new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.com"));
        adapter.RaisePermissionRequested(args);

        Assert.Equal(PermissionState.Deny, args.State);
        Assert.Empty(order);
    }

    [Fact]
    public void Permission_profile_default_falls_back_to_existing_pipeline()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        var order = new List<string>();
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((_, _) =>
                new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = "profile-default",
                    SessionDecisionOverride = new WebViewShellSessionDecision(WebViewShellSessionScope.Shared, "profile-default"),
                    DefaultPermissionDecision = WebViewPermissionProfileDecision.DefaultFallback()
                }),
            PermissionPolicy = new DelegatePermissionPolicy((_, e) =>
            {
                order.Add("policy");
                e.State = PermissionState.Allow;
            }),
            PermissionHandler = (_, e) =>
            {
                order.Add("handler");
                e.State = PermissionState.Deny;
            }
        });

        var args = new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.com"));
        adapter.RaisePermissionRequested(args);

        Assert.Equal(new[] { "policy", "handler" }, order);
        Assert.Equal(PermissionState.Deny, args.State);
    }

    [Fact]
    public void Profile_resolution_failure_isolated_and_reports_error_metadata()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        WebViewShellPolicyErrorEventArgs? observedError = null;
        var diagnostics = new List<WebViewSessionPermissionProfileDiagnosticEventArgs>();
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((ctx, _) =>
            {
                if (ctx.PermissionKind is not null)
                    throw new InvalidOperationException("profile resolver failed");

                return new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = "root-profile",
                    SessionDecisionOverride = new WebViewShellSessionDecision(WebViewShellSessionScope.Shared, "root")
                };
            }),
            PermissionHandler = (_, e) => e.State = PermissionState.Deny,
            PolicyErrorHandler = (_, err) => observedError = err
        });

        shell.SessionPermissionProfileEvaluated += (_, e) => diagnostics.Add(e);

        var args = new PermissionRequestedEventArgs(WebViewPermissionKind.Microphone, new Uri("https://example.com"));
        adapter.RaisePermissionRequested(args);

        Assert.NotNull(observedError);
        Assert.Equal(WebViewShellPolicyDomain.Permission, observedError!.Domain);
        Assert.Equal(PermissionState.Deny, args.State);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Profile_diagnostics_include_identity_permission_and_decision()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        WebViewSessionPermissionProfileDiagnosticEventArgs? observedPermissionDiagnostic = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((_, _) =>
                new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = "audit-profile",
                    SessionDecisionOverride = new WebViewShellSessionDecision(WebViewShellSessionScope.Shared, "audit-scope"),
                    PermissionDecisions = new Dictionary<WebViewPermissionKind, WebViewPermissionProfileDecision>
                    {
                        [WebViewPermissionKind.Notifications] = WebViewPermissionProfileDecision.Allow()
                    }
                })
        });

        shell.SessionPermissionProfileEvaluated += (_, e) =>
        {
            if (e.PermissionKind is not null)
                observedPermissionDiagnostic = e;
        };

        var args = new PermissionRequestedEventArgs(WebViewPermissionKind.Notifications, new Uri("https://example.com"));
        adapter.RaisePermissionRequested(args);

        Assert.Equal(PermissionState.Allow, args.State);
        Assert.NotNull(observedPermissionDiagnostic);
        Assert.Equal("audit-profile", observedPermissionDiagnostic!.ProfileIdentity);
        Assert.Equal(WebViewPermissionKind.Notifications, observedPermissionDiagnostic.PermissionKind);
        Assert.Equal(PermissionState.Allow, observedPermissionDiagnostic.PermissionDecision.State);
        Assert.True(observedPermissionDiagnostic.PermissionDecision.IsExplicit);
        DiagnosticSchemaAssertionHelper.AssertSessionProfileDiagnostic(observedPermissionDiagnostic);
    }

    [Fact]
    public async Task New_window_policy_executes_on_ui_thread()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        int? observedThreadId = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _) => observedThreadId = Environment.CurrentManagedThreadId)
        });

        await ThreadingTestHelper.RunOffThread(() =>
        {
            adapter.RaiseNewWindowRequested(new Uri("https://example.com/"));
            return Task.CompletedTask;
        });

        DispatcherTestPump.WaitUntil(dispatcher, () => observedThreadId.HasValue);
        Assert.Equal(dispatcher.UiThreadId, observedThreadId);
    }

    [Fact]
    public async Task Download_policy_executes_on_ui_thread()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        int? observedThreadId = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            DownloadPolicy = new DelegateDownloadPolicy((_, _) => observedThreadId = Environment.CurrentManagedThreadId)
        });

        await ThreadingTestHelper.RunOffThread(() =>
        {
            adapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(new Uri("https://example.com/file.bin")));
            return Task.CompletedTask;
        });

        DispatcherTestPump.WaitUntil(dispatcher, () => observedThreadId.HasValue);
        Assert.Equal(dispatcher.UiThreadId, observedThreadId);
    }

    [Fact]
    public async Task Permission_policy_executes_on_ui_thread()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        int? observedThreadId = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            PermissionPolicy = new DelegatePermissionPolicy((_, _) => observedThreadId = Environment.CurrentManagedThreadId)
        });

        await ThreadingTestHelper.RunOffThread(() =>
        {
            adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(WebViewPermissionKind.Camera));
            return Task.CompletedTask;
        });

        DispatcherTestPump.WaitUntil(dispatcher, () => observedThreadId.HasValue);
        Assert.Equal(dispatcher.UiThreadId, observedThreadId);
    }

    [Fact]
    public async Task DevTools_policy_allow_executes_open_close_and_query_operations()
    {
        using var webView = new DevToolsTrackingWebView();
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            DevToolsPolicy = new DelegateDevToolsPolicy((_, _) => WebViewShellDevToolsDecision.Allow())
        });

        var opened = await shell.OpenDevToolsAsync();
        var openState = await shell.IsDevToolsOpenAsync();
        var closed = await shell.CloseDevToolsAsync();
        var closedState = await shell.IsDevToolsOpenAsync();

        Assert.True(opened);
        Assert.True(openState);
        Assert.True(closed);
        Assert.False(closedState);
        Assert.Equal(1, webView.OpenDevToolsCallCount);
        Assert.Equal(1, webView.CloseDevToolsCallCount);
        Assert.Equal(2, webView.IsDevToolsOpenCallCount);
    }

    [Fact]
    public async Task DevTools_policy_deny_blocks_operation_and_reports_error()
    {
        using var webView = new DevToolsTrackingWebView();
        WebViewShellPolicyErrorEventArgs? observedError = null;
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            DevToolsPolicy = new DelegateDevToolsPolicy((_, _) => WebViewShellDevToolsDecision.Deny("devtools-disabled")),
            PolicyErrorHandler = (_, error) => observedError = error
        });

        var opened = await shell.OpenDevToolsAsync();
        var openState = await shell.IsDevToolsOpenAsync();

        Assert.False(opened);
        Assert.False(openState);
        Assert.Equal(0, webView.OpenDevToolsCallCount);
        Assert.Equal(0, webView.IsDevToolsOpenCallCount);
        Assert.NotNull(observedError);
        Assert.Equal(WebViewShellPolicyDomain.DevTools, observedError!.Domain);
    }

    [Fact]
    public async Task Command_policy_allow_executes_underlying_command_manager()
    {
        var commandManager = new TrackingCommandManager();
        using var webView = new DevToolsTrackingWebView
        {
            CommandManager = commandManager
        };
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            CommandPolicy = new DelegateCommandPolicy((_, _) => WebViewShellCommandDecision.Allow())
        });

        var executed = await shell.ExecuteCommandAsync(WebViewCommand.Copy);

        Assert.True(executed);
        Assert.Equal([WebViewCommand.Copy], commandManager.ExecutedCommands);
    }

    [Fact]
    public async Task Command_policy_deny_blocks_execution_and_reports_error()
    {
        var commandManager = new TrackingCommandManager();
        using var webView = new DevToolsTrackingWebView
        {
            CommandManager = commandManager
        };

        WebViewShellPolicyErrorEventArgs? observedError = null;
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            CommandPolicy = new DelegateCommandPolicy((_, _) => WebViewShellCommandDecision.Deny("command-blocked")),
            PolicyErrorHandler = (_, error) => observedError = error
        });

        var executed = await shell.ExecuteCommandAsync(WebViewCommand.Paste);

        Assert.False(executed);
        Assert.Empty(commandManager.ExecutedCommands);
        Assert.NotNull(observedError);
        Assert.Equal(WebViewShellPolicyDomain.Command, observedError!.Domain);
    }

    [Fact]
    public async Task Command_execution_reports_failure_when_manager_is_missing()
    {
        using var webView = new DevToolsTrackingWebView();
        WebViewShellPolicyErrorEventArgs? observedError = null;
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions
        {
            CommandPolicy = new DelegateCommandPolicy((_, _) => WebViewShellCommandDecision.Allow()),
            PolicyErrorHandler = (_, error) => observedError = error
        });

        var executed = await shell.ExecuteCommandAsync(WebViewCommand.Undo);

        Assert.False(executed);
        Assert.NotNull(observedError);
        Assert.Equal(WebViewShellPolicyDomain.Command, observedError!.Domain);
    }

    [Fact]
    public void Disposing_shell_experience_unsubscribes_handlers()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);

        var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            PermissionHandler = (_, e) => e.State = PermissionState.Allow
        });

        shell.Dispose();

        PermissionRequestedEventArgs? observedArgs = null;
        core.PermissionRequested += (_, e) => observedArgs = e;

        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(WebViewPermissionKind.Camera));

        Assert.NotNull(observedArgs);
        Assert.Equal(PermissionState.Default, observedArgs!.State);
    }

    [Fact]
    public void Host_capability_calls_without_bridge_return_deterministic_deny()
    {
        using var webView = new DevToolsTrackingWebView();
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions());

        var write = shell.WriteClipboardText("hello");
        var open = shell.ShowOpenFileDialog(new WebViewOpenFileDialogRequest { Title = "Open" });
        var save = shell.ShowSaveFileDialog(new WebViewSaveFileDialogRequest { Title = "Save", SuggestedFileName = "a.txt" });
        var notify = shell.ShowNotification(new WebViewNotificationRequest { Title = "t", Message = "m" });

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, write.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, open.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, save.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, notify.Outcome);
        Assert.Equal("clipboard.write", write.CapabilityId);
        Assert.Equal("filesystem.pick", open.CapabilityId);
        Assert.Equal("filesystem.pick", save.CapabilityId);
        Assert.Equal("notification.post", notify.CapabilityId);
        Assert.Equal(WebViewCapabilityPolicyDecisionKind.Deny, write.PolicyDecision.Kind);
        Assert.Equal("host-capability-bridge-not-configured", write.DenyReason);
        Assert.Equal("host-capability-bridge-not-configured", open.DenyReason);
        Assert.Equal("host-capability-bridge-not-configured", save.DenyReason);
        Assert.Equal("host-capability-bridge-not-configured", notify.DenyReason);
    }

    [Fact]
    public void TryGetManagedWindow_returns_false_for_unknown_id()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions());

        var found = shell.TryGetManagedWindow(Guid.NewGuid(), out var managed);

        Assert.False(found);
        Assert.Null(managed);
    }

    [Fact]
    public void TryGetManagedWindow_returns_true_for_tracked_window()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        DevToolsTrackingWebView? createdChild = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) => WebViewNewWindowStrategyDecision.ManagedWindow()),
            ManagedWindowFactory = _ =>
            {
                createdChild = new DevToolsTrackingWebView();
                return createdChild;
            }
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/managed"));
        DispatcherTestPump.WaitUntil(dispatcher, () => shell.ManagedWindowCount == 1);

        var windowId = Assert.Single(shell.GetManagedWindowIds());
        var found = shell.TryGetManagedWindow(windowId, out var managed);

        Assert.True(found);
        Assert.Same(createdChild, managed);
    }

    [Fact]
    public void DelegateMenuPruningPolicy_executes_delegate_and_returns_decision()
    {
        using var webView = new DevToolsTrackingWebView();
        var policy = new DelegateMenuPruningPolicy((_, ctx) =>
            WebViewMenuPruningDecision.Allow(ctx.RequestedMenuModel));

        var request = new WebViewMenuModelRequest
        {
            Items = [new WebViewMenuItemModel { Id = "file", Label = "File", IsEnabled = true }]
        };
        var context = new WebViewMenuPruningPolicyContext(
            RootWindowId: Guid.NewGuid(),
            TargetWindowId: null,
            RequestedMenuModel: request,
            CurrentEffectiveMenuModel: null,
            ProfileIdentity: null,
            ProfilePermissionDecision: null);

        var decision = policy.Decide(webView, context);

        Assert.True(decision.IsAllowed);
        Assert.Same(request, decision.EffectiveMenuModel);
    }

    private sealed class DevToolsTrackingWebView : IWebView
    {
        public Uri Source { get; set; } = new("about:blank");
        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public bool IsLoading => false;
        public Guid ChannelId { get; } = Guid.NewGuid();

        public int OpenDevToolsCallCount { get; private set; }
        public int CloseDevToolsCallCount { get; private set; }
        public int IsDevToolsOpenCallCount { get; private set; }
        public ICommandManager? CommandManager { get; init; }
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
        public Task<INativeHandle?> TryGetWebViewHandleAsync()
            => Task.FromResult<INativeHandle?>(null);
        public IWebViewRpcService? Rpc => null;
        public IBridgeService Bridge => throw new NotSupportedException();
        public IBridgeTracer? BridgeTracer { get; set; }
        public Task<byte[]> CaptureScreenshotAsync() => Task.FromException<byte[]>(new NotSupportedException());
        public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => Task.FromException<byte[]>(new NotSupportedException());
        public Task<double> GetZoomFactorAsync() => Task.FromResult(1.0);
        public Task SetZoomFactorAsync(double zoomFactor) => Task.CompletedTask;
        public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
            => Task.FromException<FindInPageEventArgs>(new NotSupportedException());
        public Task StopFindInPageAsync(bool clearHighlights = true) => Task.CompletedTask;
        public Task<string> AddPreloadScriptAsync(string javaScript) => Task.FromException<string>(new NotSupportedException());
        public Task RemovePreloadScriptAsync(string scriptId) => Task.FromException(new NotSupportedException());

        public Task OpenDevToolsAsync()
        {
            OpenDevToolsCallCount++;
            _isDevToolsOpen = true;
            return Task.CompletedTask;
        }

        public Task CloseDevToolsAsync()
        {
            CloseDevToolsCallCount++;
            _isDevToolsOpen = false;
            return Task.CompletedTask;
        }

        public Task<bool> IsDevToolsOpenAsync()
        {
            IsDevToolsOpenCallCount++;
            return Task.FromResult(_isDevToolsOpen);
        }

        public void Dispose()
        {
        }
    }

    private sealed class TrackingCommandManager : ICommandManager
    {
        public List<WebViewCommand> ExecutedCommands { get; } = [];

        public Task CopyAsync() => Track(WebViewCommand.Copy);
        public Task CutAsync() => Track(WebViewCommand.Cut);
        public Task PasteAsync() => Track(WebViewCommand.Paste);
        public Task SelectAllAsync() => Track(WebViewCommand.SelectAll);
        public Task UndoAsync() => Track(WebViewCommand.Undo);
        public Task RedoAsync() => Track(WebViewCommand.Redo);

        private Task Track(WebViewCommand command)
        {
            ExecutedCommands.Add(command);
            return Task.CompletedTask;
        }
    }
}
