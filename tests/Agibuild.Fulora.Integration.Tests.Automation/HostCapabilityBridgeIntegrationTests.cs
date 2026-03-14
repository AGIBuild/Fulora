using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agibuild.Fulora;
using Agibuild.Fulora.Shell;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

public sealed class HostCapabilityBridgeIntegrationTests
{
    [AvaloniaFact]
    public void Host_capability_bridge_representative_flow_enforces_policy_and_returns_typed_results()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var provider = new IntegrationHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new IntegrationCapabilityPolicy());
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        var policyErrors = new List<WebViewShellPolicyErrorEventArgs>();
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) => WebViewNewWindowStrategyDecision.ExternalBrowser()),
            PolicyErrorHandler = (_, error) => policyErrors.Add(error)
        });

        var read = shell.ReadClipboardText();
        var write = shell.WriteClipboardText("integration-value");
        var open = shell.ShowOpenFileDialog(new WebViewOpenFileDialogRequest { Title = "Open integration file" });
        var save = shell.ShowSaveFileDialog(new WebViewSaveFileDialogRequest { SuggestedFileName = "integration.txt" });
        var deniedNotification = shell.ShowNotification(new WebViewNotificationRequest
        {
            Title = "Denied",
            Message = "Notification blocked in policy"
        });
        var menu = shell.ApplyMenuModel(new WebViewMenuModelRequest
        {
            Items =
            [
                new WebViewMenuItemModel
                {
                    Id = "file",
                    Label = "File"
                }
            ]
        });
        var tray = shell.UpdateTrayState(new WebViewTrayStateRequest
        {
            IsVisible = true,
            Tooltip = "integration-tray"
        });
        var deniedAction = shell.ExecuteSystemAction(new WebViewSystemActionRequest
        {
            Action = WebViewSystemAction.Restart
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/external"));
        dispatcher.RunAll();

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, read.Outcome);
        Assert.True(read.IsAllowed && read.IsSuccess);
        Assert.Equal("integration-clipboard", read.Value);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, write.Outcome);
        Assert.True(write.IsAllowed && write.IsSuccess);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, open.Outcome);
        Assert.True(open.IsAllowed && open.IsSuccess);
        Assert.Single(open.Value!.Paths);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, save.Outcome);
        Assert.True(save.IsAllowed && save.IsSuccess);
        Assert.False(save.Value!.IsCanceled);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, deniedNotification.Outcome);
        Assert.False(deniedNotification.IsAllowed);
        Assert.False(deniedNotification.IsSuccess);
        Assert.Equal("notification-disabled", deniedNotification.DenyReason);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, menu.Outcome);
        Assert.True(menu.IsAllowed && menu.IsSuccess);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, tray.Outcome);
        Assert.True(tray.IsAllowed && tray.IsSuccess);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, deniedAction.Outcome);
        Assert.False(deniedAction.IsAllowed);
        Assert.Equal("system-action-disabled", deniedAction.DenyReason);

        Assert.Single(provider.ExternalOpens);
        Assert.Equal(new Uri("https://example.com/external"), provider.ExternalOpens[0]);
        Assert.Equal(0, adapter.NavigateCallCount);
        Assert.NotEmpty(provider.AppliedMenus);
        Assert.True(provider.LastTrayVisible);

        Assert.Equal(9, diagnostics.Count);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, diagnostics[4].Outcome);
        Assert.Equal(WebViewHostCapabilityOperation.NotificationShow, diagnostics[4].Operation);
        Assert.Equal("notification-disabled", diagnostics[4].DenyReason);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, diagnostics[5].Outcome);
        Assert.Equal(WebViewHostCapabilityOperation.MenuApplyModel, diagnostics[5].Operation);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, diagnostics[6].Outcome);
        Assert.Equal(WebViewHostCapabilityOperation.TrayUpdateState, diagnostics[6].Operation);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, diagnostics[7].Outcome);
        Assert.Equal(WebViewHostCapabilityOperation.SystemActionExecute, diagnostics[7].Operation);
        Assert.Equal("system-action-disabled", diagnostics[7].DenyReason);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, diagnostics[8].Outcome);
        Assert.Equal(WebViewHostCapabilityOperation.ExternalOpen, diagnostics[8].Operation);
        var expectedRootWindowId = diagnostics[0].RootWindowId;
        Assert.All(diagnostics, d => DiagnosticSchemaAssertionHelper.AssertHostCapabilityDiagnostic(d, expectedRootWindowId));

        Assert.Single(policyErrors);
        Assert.Equal(WebViewShellPolicyDomain.SystemIntegration, policyErrors[0].Domain);
        Assert.Contains("system-action-disabled", policyErrors[0].Exception.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Host_capability_bridge_stress_external_open_cycles_remain_deterministic()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);
        var provider = new IntegrationHostCapabilityProvider();

        const int iterations = 30;
        for (var i = 0; i < iterations; i++)
        {
            var current = i;
            var bridge = new WebViewHostCapabilityBridge(
                provider,
                new IntegrationCapabilityPolicy(uri =>
                {
                    if (uri is null)
                        return WebViewHostCapabilityDecision.Allow();
                    return current % 4 == 0
                        ? WebViewHostCapabilityDecision.Deny("stress-deny")
                        : WebViewHostCapabilityDecision.Allow();
                }));

            using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
            {
                HostCapabilityBridge = bridge,
                NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) => WebViewNewWindowStrategyDecision.ExternalBrowser())
            });

            await ThreadingTestHelper.RunOffThread(() =>
            {
                adapter.RaiseNewWindowRequested(new Uri($"https://example.com/stress/{current}"));
                return Task.CompletedTask;
            });

            dispatcher.RunAll();
            Assert.Equal(0, adapter.NavigateCallCount);
            Assert.Equal(0, shell.ManagedWindowCount);
        }

        Assert.Equal(iterations - (iterations / 4 + 1), provider.ExternalOpens.Count);
    }

    [AvaloniaFact]
    public void Shell_product_experience_closure_file_menu_and_permission_recovery_is_deterministic()
    {
        var dispatcher = new TestDispatcher();
        var adapter = new MockWebViewAdapterFull();
        using var core = new WebViewCore(adapter, dispatcher);
        var provider = new IntegrationHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new IntegrationCapabilityPolicy());

        using (var deniedShell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            PermissionPolicy = new DelegatePermissionPolicy((_, e) => e.State = PermissionState.Deny)
        }))
        {
            var open = deniedShell.ShowOpenFileDialog(new WebViewOpenFileDialogRequest { Title = "Open product-flow file" });
            var save = deniedShell.ShowSaveFileDialog(new WebViewSaveFileDialogRequest { SuggestedFileName = "product-flow.txt" });
            var menu = deniedShell.ApplyMenuModel(new WebViewMenuModelRequest
            {
                Items =
                [
                    new WebViewMenuItemModel
                    {
                        Id = "file",
                        Label = "File"
                    }
                ]
            });
            var deniedPermission = new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.com/denied"));
            adapter.RaisePermissionRequested(deniedPermission);

            Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, open.Outcome);
            Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, save.Outcome);
            Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, menu.Outcome);
            Assert.Equal(PermissionState.Deny, deniedPermission.State);
        }

        using (var recoveredShell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            PermissionPolicy = new DelegatePermissionPolicy((_, e) => e.State = PermissionState.Allow)
        }))
        {
            var open = recoveredShell.ShowOpenFileDialog(new WebViewOpenFileDialogRequest { Title = "Open recovered file" });
            var menu = recoveredShell.ApplyMenuModel(new WebViewMenuModelRequest
            {
                Items =
                [
                    new WebViewMenuItemModel
                    {
                        Id = "help",
                        Label = "Help"
                    }
                ]
            });
            var allowedPermission = new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.com/recovered"));
            adapter.RaisePermissionRequested(allowedPermission);

            Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, open.Outcome);
            Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, menu.Outcome);
            Assert.Equal(PermissionState.Allow, allowedPermission.State);
        }

        Assert.Equal(2, provider.AppliedMenus.Count);
    }

    [AvaloniaFact]
    public void Host_capability_policy_failure_blocks_provider_and_reports_external_open_domain_error()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);
        var provider = new IntegrationHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(
            provider,
            new IntegrationCapabilityPolicy(_ => throw new InvalidOperationException("external-policy-fault")));
        var policyErrors = new List<WebViewShellPolicyErrorEventArgs>();

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) => WebViewNewWindowStrategyDecision.ExternalBrowser()),
            PolicyErrorHandler = (_, error) => policyErrors.Add(error)
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/policy-fault"));
        dispatcher.RunAll();

        Assert.Empty(provider.ExternalOpens);
        Assert.Equal(0, adapter.NavigateCallCount);
        Assert.Single(policyErrors);
        Assert.Equal(WebViewShellPolicyDomain.ExternalOpen, policyErrors[0].Domain);
        Assert.True(WebViewOperationFailure.TryGetCategory(policyErrors[0].Exception, out var category));
        Assert.Equal(WebViewOperationFailureCategory.AdapterFailed, category);
    }

    [AvaloniaFact]
    public void Host_system_integration_event_roundtrip_and_menu_pruning_are_deterministic()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);
        var provider = new IntegrationHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new IntegrationCapabilityPolicy());
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        var received = new List<WebViewSystemIntegrationEventRequest>();
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            MenuPruningPolicy = new DelegateMenuPruningPolicy((_, context) =>
            {
                var effective = context.RequestedMenuModel.Items
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray();
                return WebViewMenuPruningDecision.Allow(new WebViewMenuModelRequest { Items = effective });
            })
        });
        shell.SystemIntegrationEventReceived += (_, evt) => received.Add(evt);

        var menu = shell.ApplyMenuModel(new WebViewMenuModelRequest
        {
            Items =
            [
                new WebViewMenuItemModel { Id = "file", Label = "File" },
                new WebViewMenuItemModel { Id = "file", Label = "File duplicate" },
                new WebViewMenuItemModel { Id = "help", Label = "Help" }
            ]
        });
        var trayEvent = shell.PublishSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "integration-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-main",
            Context = "clicked",
            Metadata = new Dictionary<string, string>
            {
                ["platform.source"] = "integration-test"
            }
        });

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, menu.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, trayEvent.Outcome);
        var appliedMenu = Assert.Single(provider.AppliedMenus);
        Assert.Equal(2, appliedMenu.Items.Count);
        Assert.Equal("file", appliedMenu.Items[0].Id);
        Assert.Equal("help", appliedMenu.Items[1].Id);
        Assert.Single(received);
        Assert.Equal(WebViewSystemIntegrationEventKind.TrayInteracted, received[0].Kind);
        Assert.Equal("tray-main", received[0].ItemId);
        Assert.Equal("integration-test", received[0].Metadata["platform.source"]);

        Assert.Contains(diagnostics, x => x.Operation == WebViewHostCapabilityOperation.MenuApplyModel
            && x.Outcome == WebViewHostCapabilityCallOutcome.Allow);
        Assert.Contains(diagnostics, x => x.Operation == WebViewHostCapabilityOperation.TrayInteractionEventDispatch
            && x.Outcome == WebViewHostCapabilityCallOutcome.Allow);
    }

    [AvaloniaFact]
    public void Host_system_integration_federated_roundtrip_enforces_showabout_whitelist_and_metadata_boundary()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);
        var provider = new IntegrationHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new IntegrationCapabilityPolicy(allowSystemAction: true));
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        var profileDiagnostics = new List<WebViewSessionPermissionProfileDiagnosticEventArgs>();
        var received = new List<WebViewSystemIntegrationEventRequest>();
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((context, _) =>
                new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = "integration-shell-profile",
                    ProfileVersion = "2026.02.21",
                    ProfileHash = $"SHA256:{new string('A', 64)}",
                    PermissionDecisions = new Dictionary<WebViewPermissionKind, WebViewPermissionProfileDecision>
                    {
                        [WebViewPermissionKind.Other] = WebViewPermissionProfileDecision.Allow()
                    }
                }),
            MenuPruningPolicy = new DelegateMenuPruningPolicy((_, context) =>
            {
                var effective = context.RequestedMenuModel.Items
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray();
                return WebViewMenuPruningDecision.Allow(new WebViewMenuModelRequest { Items = effective });
            })
        });
        shell.SystemIntegrationEventReceived += (_, evt) => received.Add(evt);
        shell.SessionPermissionProfileEvaluated += (_, evt) => profileDiagnostics.Add(evt);

        var menu = shell.ApplyMenuModel(new WebViewMenuModelRequest
        {
            Items =
            [
                new WebViewMenuItemModel { Id = "file", Label = "File" },
                new WebViewMenuItemModel { Id = "file", Label = "File duplicate" },
                new WebViewMenuItemModel { Id = "help", Label = "Help" }
            ]
        });
        var showAbout = shell.ExecuteSystemAction(new WebViewSystemActionRequest
        {
            Action = WebViewSystemAction.ShowAbout
        });
        var invalidTrayEvent = shell.PublishSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "integration-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-invalid",
            Metadata = new Dictionary<string, string>
            {
                [""] = "invalid-metadata"
            }
        });
        var validTrayEvent = shell.PublishSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "integration-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-main",
            Context = "clicked",
            Metadata = new Dictionary<string, string>
            {
                ["platform.source"] = "integration-test"
            }
        });
        var overBudgetTrayEvent = shell.PublishSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "integration-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-budget-over",
            Metadata = new Dictionary<string, string>
            {
                ["platform.extension.a"] = new string('x', 256),
                ["platform.extension.b"] = new string('x', 256),
                ["platform.extension.c"] = new string('x', 256),
                ["platform.extension.d"] = new string('x', 256)
            }
        });

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, menu.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, showAbout.Outcome);
        Assert.Equal("system-action-not-whitelisted", showAbout.DenyReason);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, invalidTrayEvent.Outcome);
        Assert.Equal("system-integration-event-metadata-envelope-invalid", invalidTrayEvent.DenyReason);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, validTrayEvent.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, overBudgetTrayEvent.Outcome);
        Assert.Equal("system-integration-event-metadata-budget-exceeded", overBudgetTrayEvent.DenyReason);
        Assert.Equal(0, provider.SystemActionCalls);

        var appliedMenu = Assert.Single(provider.AppliedMenus);
        Assert.Equal(2, appliedMenu.Items.Count);
        Assert.Single(received);
        Assert.Equal("tray-main", received[0].ItemId);
        Assert.Equal("integration-test", received[0].Metadata["platform.source"]);

        Assert.Contains(profileDiagnostics, x =>
            x.PermissionKind == WebViewPermissionKind.Other &&
            x.ProfileIdentity == "integration-shell-profile" &&
            x.ProfileVersion == "2026.02.21" &&
            x.ProfileHash == $"sha256:{new string('a', 64)}" &&
            x.PermissionDecision.State == PermissionState.Allow);
        Assert.All(profileDiagnostics, DiagnosticSchemaAssertionHelper.AssertSessionProfileDiagnostic);

        Assert.Contains(diagnostics, x => x.Operation == WebViewHostCapabilityOperation.MenuApplyModel
            && x.Outcome == WebViewHostCapabilityCallOutcome.Allow);
        Assert.Contains(diagnostics, x => x.Operation == WebViewHostCapabilityOperation.TrayInteractionEventDispatch
            && x.Outcome == WebViewHostCapabilityCallOutcome.Deny
            && x.DenyReason == "system-integration-event-metadata-envelope-invalid");
        Assert.Contains(diagnostics, x => x.Operation == WebViewHostCapabilityOperation.TrayInteractionEventDispatch
            && x.Outcome == WebViewHostCapabilityCallOutcome.Deny
            && x.DenyReason == "system-integration-event-metadata-budget-exceeded");
        Assert.Contains(diagnostics, x => x.Operation == WebViewHostCapabilityOperation.TrayInteractionEventDispatch
            && x.Outcome == WebViewHostCapabilityCallOutcome.Allow);
        Assert.All(diagnostics, d => DiagnosticSchemaAssertionHelper.AssertHostCapabilityDiagnostic(d, d.RootWindowId));
    }

    [AvaloniaFact]
    public void Tray_payload_v2_roundtrip_with_platform_extensions_is_machine_checkable()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);
        var provider = new IntegrationHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new IntegrationCapabilityPolicy());
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        var received = new List<WebViewSystemIntegrationEventRequest>();
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge
        });
        shell.SystemIntegrationEventReceived += (_, evt) => received.Add(evt);
        var occurredAt = new DateTimeOffset(2026, 2, 25, 1, 2, 3, TimeSpan.Zero).AddTicks(5432);

        var eventResult = shell.PublishSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "integration-host",
            OccurredAtUtc = occurredAt,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-v2",
            Context = "clicked",
            Metadata = new Dictionary<string, string>
            {
                ["platform.source"] = "integration-suite",
                ["platform.visibility"] = "visible",
                ["platform.extension.alpha"] = "A"
            }
        });

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, eventResult.Outcome);
        var delivered = Assert.Single(received);
        Assert.Equal("integration-host", delivered.Source);
        Assert.Equal("tray-v2", delivered.ItemId);
        Assert.Equal("integration-suite", delivered.Metadata["platform.source"]);
        Assert.Equal("visible", delivered.Metadata["platform.visibility"]);
        Assert.Equal("A", delivered.Metadata["platform.extension.alpha"]);
        var expectedTicks = occurredAt.UtcTicks - (occurredAt.UtcTicks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(new DateTimeOffset(expectedTicks, TimeSpan.Zero), delivered.OccurredAtUtc);
        Assert.Equal(0, delivered.OccurredAtUtc.Ticks % TimeSpan.TicksPerMillisecond);

        Assert.Contains(diagnostics, x => x.Operation == WebViewHostCapabilityOperation.TrayInteractionEventDispatch
            && x.Outcome == WebViewHostCapabilityCallOutcome.Allow);
    }

    [AvaloniaFact]
    public void System_integration_diagnostic_export_protocol_is_machine_checkable()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);
        var provider = new IntegrationHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new IntegrationCapabilityPolicy(allowSystemAction: true));
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);

        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            HostCapabilityBridge = bridge,
            SystemActionWhitelist = new HashSet<WebViewSystemAction>
            {
                WebViewSystemAction.FocusMainWindow
            }
        });

        var allowedEvent = shell.PublishSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "integration-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-diagnostic-allow",
            Metadata = new Dictionary<string, string>
            {
                ["platform.source"] = "integration-test"
            }
        });
        var deniedEvent = shell.PublishSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "integration-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-diagnostic-deny",
            Metadata = new Dictionary<string, string>
            {
                [""] = "invalid-key"
            }
        });
        var deniedAction = shell.ExecuteSystemAction(new WebViewSystemActionRequest
        {
            Action = WebViewSystemAction.ShowAbout
        });
        var failedAction = shell.ExecuteSystemAction(new WebViewSystemActionRequest
        {
            Action = WebViewSystemAction.FocusMainWindow
        });

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, allowedEvent.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, deniedEvent.Outcome);
        Assert.Equal("system-integration-event-metadata-envelope-invalid", deniedEvent.DenyReason);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, deniedAction.Outcome);
        Assert.Equal("system-action-not-whitelisted", deniedAction.DenyReason);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Failure, failedAction.Outcome);

        var records = diagnostics.Select(x => x.ToExportRecord()).ToArray();
        Assert.NotEmpty(records);
        Assert.Contains(records, x => x.Operation == "tray-interaction-event-dispatch"
            && x.Outcome == "allow");
        Assert.Contains(records, x => x.Operation == "tray-interaction-event-dispatch"
            && x.Outcome == "deny"
            && x.DenyReason == "system-integration-event-metadata-envelope-invalid");
        Assert.Contains(records, x => x.Operation == "system-action-execute"
            && x.Outcome == "failure"
            && x.FailureCategory == "adapter-failed");
        Assert.All(records, x => Assert.Equal(WebViewHostCapabilityDiagnosticEventArgs.CurrentDiagnosticSchemaVersion, x.SchemaVersion));
    }

    private sealed class IntegrationCapabilityPolicy : IWebViewHostCapabilityPolicy
    {
        private readonly Func<Uri?, WebViewHostCapabilityDecision>? _externalDecision;
        private readonly bool _allowSystemAction;

        public IntegrationCapabilityPolicy(
            Func<Uri?, WebViewHostCapabilityDecision>? externalDecision = null,
            bool allowSystemAction = false)
        {
            _externalDecision = externalDecision;
            _allowSystemAction = allowSystemAction;
        }

        public WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context)
        {
            return context.Operation switch
            {
                WebViewHostCapabilityOperation.NotificationShow => WebViewHostCapabilityDecision.Deny("notification-disabled"),
                WebViewHostCapabilityOperation.SystemActionExecute when !_allowSystemAction => WebViewHostCapabilityDecision.Deny("system-action-disabled"),
                WebViewHostCapabilityOperation.ExternalOpen when _externalDecision is not null => _externalDecision(context.RequestUri),
                _ => WebViewHostCapabilityDecision.Allow()
            };
        }
    }

    private sealed class IntegrationHostCapabilityProvider : IWebViewHostCapabilityProvider
    {
        public List<Uri> ExternalOpens { get; } = [];
        public List<WebViewMenuModelRequest> AppliedMenus { get; } = [];
        public bool LastTrayVisible { get; private set; }
        public int SystemActionCalls { get; private set; }

        public string? ReadClipboardText() => "integration-clipboard";

        public void WriteClipboardText(string text)
        {
        }

        public WebViewFileDialogResult ShowOpenFileDialog(WebViewOpenFileDialogRequest request)
            => new()
            {
                IsCanceled = false,
                Paths = ["C:\\integration\\open.txt"]
            };

        public WebViewFileDialogResult ShowSaveFileDialog(WebViewSaveFileDialogRequest request)
            => new()
            {
                IsCanceled = false,
                Paths = ["C:\\integration\\save.txt"]
            };

        public void OpenExternal(Uri uri)
            => ExternalOpens.Add(uri);

        public void ShowNotification(WebViewNotificationRequest request)
        {
        }

        public void ApplyMenuModel(WebViewMenuModelRequest request)
            => AppliedMenus.Add(request);

        public void UpdateTrayState(WebViewTrayStateRequest request)
            => LastTrayVisible = request.IsVisible;

        public void ExecuteSystemAction(WebViewSystemActionRequest request)
        {
            SystemActionCalls++;
            throw new NotSupportedException("System action is policy-disabled in integration provider.");
        }
    }
}
