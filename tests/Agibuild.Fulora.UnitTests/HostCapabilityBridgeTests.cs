using System;
using System.Collections.Generic;
using System.Linq;
using Agibuild.Fulora.Shell;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class HostCapabilityBridgeTests
{
    [Fact]
    public void Typed_capability_calls_succeed_when_policy_allows()
    {
        var provider = new TestHostCapabilityProvider();
        var policy = new AllowAllPolicy();
        var bridge = new WebViewHostCapabilityBridge(provider, policy);
        var root = Guid.NewGuid();

        var read = bridge.ReadClipboardText(root);
        var write = bridge.WriteClipboardText("hello", root);
        var open = bridge.ShowOpenFileDialog(new WebViewOpenFileDialogRequest { Title = "Open" }, root);
        var save = bridge.ShowSaveFileDialog(new WebViewSaveFileDialogRequest { Title = "Save", SuggestedFileName = "a.txt" }, root);
        var notify = bridge.ShowNotification(new WebViewNotificationRequest { Title = "T", Message = "M" }, root);
        var external = bridge.OpenExternal(new Uri("https://example.com"), root);
        var menu = bridge.ApplyMenuModel(new WebViewMenuModelRequest
        {
            Items =
            [
                new WebViewMenuItemModel
                {
                    Id = "file",
                    Label = "File"
                }
            ]
        }, root);
        var tray = bridge.UpdateTrayState(new WebViewTrayStateRequest
        {
            IsVisible = true,
            Tooltip = "host-tray"
        }, root);
        var action = bridge.ExecuteSystemAction(new WebViewSystemActionRequest
        {
            Action = WebViewSystemAction.FocusMainWindow
        }, root);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, read.Outcome);
        Assert.True(read.IsAllowed && read.IsSuccess);
        Assert.Equal("from-clipboard", read.Value);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, write.Outcome);
        Assert.True(write.IsAllowed && write.IsSuccess);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, open.Outcome);
        Assert.True(open.IsAllowed && open.IsSuccess);
        Assert.Single(open.Value!.Paths);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, save.Outcome);
        Assert.True(save.IsAllowed && save.IsSuccess);
        Assert.False(save.Value!.IsCanceled);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, notify.Outcome);
        Assert.True(notify.IsAllowed && notify.IsSuccess);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, external.Outcome);
        Assert.True(external.IsAllowed && external.IsSuccess);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, menu.Outcome);
        Assert.True(menu.IsAllowed && menu.IsSuccess);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, tray.Outcome);
        Assert.True(tray.IsAllowed && tray.IsSuccess);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, action.Outcome);
        Assert.True(action.IsAllowed && action.IsSuccess);

        Assert.Equal(9, provider.CallCount);
    }

    [Fact]
    public void Denied_policy_skips_provider_and_returns_deny_reason()
    {
        var provider = new TestHostCapabilityProvider();
        var policy = new DenyAllPolicy();
        var bridge = new WebViewHostCapabilityBridge(provider, policy);

        var result = bridge.OpenExternal(new Uri("https://example.com"), Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, result.Outcome);
        Assert.False(result.IsAllowed);
        Assert.False(result.IsSuccess);
        Assert.Equal("shell.external_open", result.CapabilityId);
        Assert.Equal(WebViewCapabilityPolicyDecisionKind.Deny, result.PolicyDecision.Kind);
        Assert.Equal("denied-by-policy", result.DenyReason);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public void Provider_failure_isolated_and_classified()
    {
        var provider = new TestHostCapabilityProvider
        {
            ThrowOn = WebViewHostCapabilityOperation.ClipboardReadText
        };
        var bridge = new WebViewHostCapabilityBridge(provider, new AllowAllPolicy());
        var root = Guid.NewGuid();

        var failed = bridge.ReadClipboardText(root);
        var external = bridge.OpenExternal(new Uri("https://example.com"), root);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Failure, failed.Outcome);
        Assert.True(failed.IsAllowed);
        Assert.False(failed.IsSuccess);
        Assert.NotNull(failed.Error);
        Assert.True(WebViewOperationFailure.TryGetCategory(failed.Error!, out var category));
        Assert.Equal(WebViewOperationFailureCategory.AdapterFailed, category);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, external.Outcome);
        Assert.True(external.IsAllowed);
        Assert.True(external.IsSuccess);
    }

    [Fact]
    public void Policy_exception_returns_failure_without_provider_execution()
    {
        var provider = new TestHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new ThrowingPolicy());

        var result = bridge.ShowNotification(
            new WebViewNotificationRequest { Title = "A", Message = "B" },
            Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Failure, result.Outcome);
        Assert.False(result.IsAllowed);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.True(WebViewOperationFailure.TryGetCategory(result.Error!, out var category));
        Assert.Equal(WebViewOperationFailureCategory.AdapterFailed, category);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public void Capability_diagnostics_are_machine_checkable_with_allow_deny_and_failure_outcomes()
    {
        var provider = new TestHostCapabilityProvider
        {
            ThrowOn = WebViewHostCapabilityOperation.ExternalOpen
        };
        var bridge = new WebViewHostCapabilityBridge(provider, new SelectivePolicy());
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);
        var root = Guid.NewGuid();

        var read = bridge.ReadClipboardText(root);
        var denied = bridge.ShowNotification(new WebViewNotificationRequest { Title = "T", Message = "M" }, root);
        var failed = bridge.OpenExternal(new Uri("https://example.com"), root);
        var menu = bridge.ApplyMenuModel(new WebViewMenuModelRequest
        {
            Items =
            [
                new WebViewMenuItemModel
                {
                    Id = "view",
                    Label = "View"
                }
            ]
        }, root);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, read.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, denied.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Failure, failed.Outcome);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, menu.Outcome);

        Assert.Equal(4, diagnostics.Count);
        Assert.Equal(WebViewHostCapabilityOperation.ClipboardReadText, diagnostics[0].Operation);
        Assert.Equal("clipboard.read", diagnostics[0].CapabilityId);
        Assert.Equal("host-bridge", diagnostics[0].SourceComponent);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, diagnostics[0].Outcome);
        Assert.True(diagnostics[0].WasAuthorized);
        Assert.Equal(WebViewCapabilityPolicyDecisionKind.Allow, diagnostics[0].PolicyDecision.Kind);

        Assert.Equal(WebViewHostCapabilityOperation.NotificationShow, diagnostics[1].Operation);
        Assert.Equal("notification.post", diagnostics[1].CapabilityId);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, diagnostics[1].Outcome);
        Assert.False(diagnostics[1].WasAuthorized);
        Assert.Equal(WebViewCapabilityPolicyDecisionKind.Deny, diagnostics[1].PolicyDecision.Kind);
        Assert.Equal("notification-denied", diagnostics[1].DenyReason);

        Assert.Equal(WebViewHostCapabilityOperation.ExternalOpen, diagnostics[2].Operation);
        Assert.Equal("shell.external_open", diagnostics[2].CapabilityId);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Failure, diagnostics[2].Outcome);
        Assert.True(diagnostics[2].WasAuthorized);
        Assert.Equal(WebViewCapabilityPolicyDecisionKind.Allow, diagnostics[2].PolicyDecision.Kind);
        Assert.Equal(WebViewOperationFailureCategory.AdapterFailed, diagnostics[2].FailureCategory);

        Assert.Equal(WebViewHostCapabilityOperation.MenuApplyModel, diagnostics[3].Operation);
        Assert.Equal("window.chrome.modify", diagnostics[3].CapabilityId);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, diagnostics[3].Outcome);
        Assert.True(diagnostics[3].WasAuthorized);

        Assert.All(diagnostics, d =>
        {
            DiagnosticSchemaAssertionHelper.AssertHostCapabilityDiagnostic(d, root);
        });
        Assert.Equal(1, WebViewHostCapabilityDiagnosticEventArgs.CurrentDiagnosticSchemaVersion);
    }

    [Fact]
    public void Capability_diagnostic_export_records_are_machine_readable_and_preserve_taxonomy()
    {
        var provider = new TestHostCapabilityProvider
        {
            ThrowOn = WebViewHostCapabilityOperation.ExternalOpen
        };
        var bridge = new WebViewHostCapabilityBridge(provider, new SelectivePolicy());
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);
        var root = Guid.NewGuid();

        _ = bridge.ReadClipboardText(root);
        _ = bridge.ShowNotification(new WebViewNotificationRequest { Title = "T", Message = "M" }, root);
        _ = bridge.OpenExternal(new Uri("https://example.com"), root);

        var records = diagnostics
            .Select(x => x.ToExportRecord())
            .ToArray();
        Assert.Equal(3, records.Length);

        Assert.Equal(WebViewHostCapabilityDiagnosticEventArgs.CurrentDiagnosticSchemaVersion, records[0].SchemaVersion);
        Assert.True(Guid.TryParseExact(records[0].CorrelationId, "D", out _));
        Assert.True(Guid.TryParseExact(records[0].RootWindowId, "D", out _));
        Assert.Equal("clipboard-read-text", records[0].Operation);
        Assert.Equal("clipboard.read", records[0].CapabilityId);
        Assert.Equal("host-bridge", records[0].SourceComponent);
        Assert.Equal("allow", records[0].PolicyDecision);
        Assert.Equal("allow", records[0].Outcome);
        Assert.True(records[0].WasAuthorized);
        Assert.Null(records[0].DenyReason);
        Assert.Null(records[0].FailureCategory);

        Assert.Equal("notification-show", records[1].Operation);
        Assert.Equal("notification.post", records[1].CapabilityId);
        Assert.Equal("deny", records[1].PolicyDecision);
        Assert.Equal("deny", records[1].Outcome);
        Assert.False(records[1].WasAuthorized);
        Assert.Equal("notification-denied", records[1].DenyReason);
        Assert.Null(records[1].FailureCategory);

        Assert.Equal("external-open", records[2].Operation);
        Assert.Equal("shell.external_open", records[2].CapabilityId);
        Assert.Equal("allow", records[2].PolicyDecision);
        Assert.Equal("failure", records[2].Outcome);
        Assert.True(records[2].WasAuthorized);
        Assert.Null(records[2].DenyReason);
        Assert.Equal("adapter-failed", records[2].FailureCategory);
    }

    [Fact]
    public void Denied_system_integration_operations_skip_provider_execution()
    {
        var provider = new TestHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new DenySystemIntegrationPolicy());
        var root = Guid.NewGuid();

        var menu = bridge.ApplyMenuModel(new WebViewMenuModelRequest
        {
            Items =
            [
                new WebViewMenuItemModel
                {
                    Id = "file",
                    Label = "File"
                }
            ]
        }, root);
        var tray = bridge.UpdateTrayState(new WebViewTrayStateRequest
        {
            IsVisible = true
        }, root);
        var action = bridge.ExecuteSystemAction(new WebViewSystemActionRequest
        {
            Action = WebViewSystemAction.FocusMainWindow
        }, root);
        var clipboard = bridge.ReadClipboardText(root);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, menu.Outcome);
        Assert.Equal("system-integration-denied", menu.DenyReason);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, tray.Outcome);
        Assert.Equal("system-integration-denied", tray.DenyReason);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, action.Outcome);
        Assert.Equal("system-integration-denied", action.DenyReason);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, clipboard.Outcome);
        Assert.Equal("from-clipboard", clipboard.Value);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void System_integration_provider_failure_does_not_break_other_system_integration_operations()
    {
        var provider = new TestHostCapabilityProvider
        {
            ThrowOn = WebViewHostCapabilityOperation.MenuApplyModel
        };
        var bridge = new WebViewHostCapabilityBridge(provider, new AllowAllPolicy());
        var root = Guid.NewGuid();

        var menu = bridge.ApplyMenuModel(new WebViewMenuModelRequest
        {
            Items =
            [
                new WebViewMenuItemModel
                {
                    Id = "view",
                    Label = "View"
                }
            ]
        }, root);
        var tray = bridge.UpdateTrayState(new WebViewTrayStateRequest
        {
            IsVisible = true,
            Tooltip = "tray-ok"
        }, root);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Failure, menu.Outcome);
        Assert.True(menu.IsAllowed);
        Assert.NotNull(menu.Error);
        Assert.True(WebViewOperationFailure.TryGetCategory(menu.Error!, out var category));
        Assert.Equal(WebViewOperationFailureCategory.AdapterFailed, category);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, tray.Outcome);
        Assert.True(tray.IsAllowed && tray.IsSuccess);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void Inbound_system_integration_event_dispatch_is_policy_first_and_machine_checkable()
    {
        var provider = new TestHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new AllowAllPolicy());
        var root = Guid.NewGuid();
        var dispatched = new List<WebViewSystemIntegrationEventRequest>();
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        bridge.SystemIntegrationEventDispatched += (_, e) => dispatched.Add(e);
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "unit-test-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-main",
            Context = "clicked",
            Metadata = new Dictionary<string, string>
            {
                ["platform.source"] = "unit-test"
            }
        }, root);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, eventResult.Outcome);
        Assert.Single(dispatched);
        Assert.Equal("tray-main", dispatched[0].ItemId);
        Assert.Equal("unit-test", dispatched[0].Metadata["platform.source"]);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(WebViewHostCapabilityOperation.TrayInteractionEventDispatch, diagnostic.Operation);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, diagnostic.Outcome);
        Assert.True(diagnostic.WasAuthorized);
    }

    [Fact]
    public void Denied_inbound_system_integration_event_never_reaches_dispatch_subscribers()
    {
        var provider = new TestHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new DenyInboundEventPolicy());
        var dispatched = 0;
        bridge.SystemIntegrationEventDispatched += (_, _) => dispatched++;

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "unit-test-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.MenuItemInvoked,
            ItemId = "menu-file-open"
        }, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, eventResult.Outcome);
        Assert.Equal("inbound-event-denied", eventResult.DenyReason);
        Assert.Equal(0, dispatched);
    }

    [Fact]
    public void Invalid_inbound_system_integration_metadata_is_denied_before_policy_and_dispatch()
    {
        var provider = new TestHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new AllowAllPolicy());
        var dispatched = 0;
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        bridge.SystemIntegrationEventDispatched += (_, _) => dispatched++;
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "unit-test-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-main",
            Metadata = new Dictionary<string, string>
            {
                [""] = "invalid-key"
            }
        }, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, eventResult.Outcome);
        Assert.Equal("system-integration-event-metadata-envelope-invalid", eventResult.DenyReason);
        Assert.Equal(0, dispatched);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(WebViewHostCapabilityOperation.TrayInteractionEventDispatch, diagnostic.Operation);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, diagnostic.Outcome);
        Assert.False(diagnostic.WasAuthorized);
        Assert.Equal("system-integration-event-metadata-envelope-invalid", diagnostic.DenyReason);
    }

    [Fact]
    public void Exact_budget_inbound_system_integration_metadata_is_allowed_and_dispatched()
    {
        var provider = new TestHostCapabilityProvider();
        var policy = new CountingAllowPolicy();
        var bridge = new WebViewHostCapabilityBridge(provider, policy);
        var dispatched = 0;
        bridge.SystemIntegrationEventDispatched += (_, _) => dispatched++;

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "unit-test-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-budget-edge",
            Metadata = new Dictionary<string, string>
            {
                ["platform.extension.a"] = new string('x', 236),
                ["platform.extension.b"] = new string('x', 236),
                ["platform.extension.c"] = new string('x', 236),
                ["platform.extension.d"] = new string('x', 236)
            }
        }, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, eventResult.Outcome);
        Assert.Equal(1, dispatched);
        Assert.Equal(1, policy.EvaluateCalls);
    }

    [Fact]
    public void Over_budget_inbound_system_integration_metadata_is_denied_before_policy_and_dispatch()
    {
        var provider = new TestHostCapabilityProvider();
        var policy = new CountingAllowPolicy();
        var bridge = new WebViewHostCapabilityBridge(provider, policy);
        var dispatched = 0;
        var diagnostics = new List<WebViewHostCapabilityDiagnosticEventArgs>();
        bridge.SystemIntegrationEventDispatched += (_, _) => dispatched++;
        bridge.CapabilityCallCompleted += (_, e) => diagnostics.Add(e);

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "unit-test-host",
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
        }, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, eventResult.Outcome);
        Assert.Equal("system-integration-event-metadata-budget-exceeded", eventResult.DenyReason);
        Assert.Equal(0, dispatched);
        Assert.Equal(0, policy.EvaluateCalls);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(WebViewHostCapabilityOperation.TrayInteractionEventDispatch, diagnostic.Operation);
        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, diagnostic.Outcome);
        Assert.False(diagnostic.WasAuthorized);
        Assert.Equal("system-integration-event-metadata-budget-exceeded", diagnostic.DenyReason);
    }

    [Fact]
    public void Inbound_metadata_budget_configuration_outside_bounds_is_rejected_deterministically()
    {
        var provider = new TestHostCapabilityProvider();

        var low = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new WebViewHostCapabilityBridge(
                provider,
                new AllowAllPolicy(),
                new WebViewHostCapabilityBridgeOptions
                {
                    SystemIntegrationMetadataTotalLength = WebViewHostCapabilityBridgeOptions.MinSystemIntegrationMetadataTotalLength - 1
                }));
        Assert.Contains("System integration metadata total length must be within", low.Message, StringComparison.Ordinal);

        var high = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new WebViewHostCapabilityBridge(
                provider,
                new AllowAllPolicy(),
                new WebViewHostCapabilityBridgeOptions
                {
                    SystemIntegrationMetadataTotalLength = WebViewHostCapabilityBridgeOptions.MaxSystemIntegrationMetadataTotalLength + 1
                }));
        Assert.Contains("System integration metadata total length must be within", high.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inbound_metadata_budget_uses_configured_in_range_value_deterministically()
    {
        var provider = new TestHostCapabilityProvider();
        var policy = new CountingAllowPolicy();
        var bridge = new WebViewHostCapabilityBridge(
            provider,
            policy,
            new WebViewHostCapabilityBridgeOptions
            {
                SystemIntegrationMetadataTotalLength = 1200
            });

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "unit-test-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-budget-configured",
            Metadata = new Dictionary<string, string>
            {
                ["platform.extension.a"] = new string('x', 256),
                ["platform.extension.b"] = new string('x', 256),
                ["platform.extension.c"] = new string('x', 256),
                ["platform.extension.d"] = new string('x', 256)
            }
        }, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, eventResult.Outcome);
        Assert.Equal(1, policy.EvaluateCalls);
    }

    [Fact]
    public void Non_platform_metadata_key_is_denied_before_policy_and_dispatch()
    {
        var provider = new TestHostCapabilityProvider();
        var policy = new CountingAllowPolicy();
        var bridge = new WebViewHostCapabilityBridge(provider, policy);
        var dispatched = 0;
        bridge.SystemIntegrationEventDispatched += (_, _) => dispatched++;

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "unit-test-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-main",
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "invalid-namespace"
            }
        }, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, eventResult.Outcome);
        Assert.Equal("system-integration-event-metadata-namespace-invalid", eventResult.DenyReason);
        Assert.Equal(0, dispatched);
        Assert.Equal(0, policy.EvaluateCalls);
    }

    [Fact]
    public void Unregistered_platform_metadata_key_is_denied_before_policy_and_dispatch()
    {
        var provider = new TestHostCapabilityProvider();
        var policy = new CountingAllowPolicy();
        var bridge = new WebViewHostCapabilityBridge(provider, policy);
        var dispatched = 0;
        bridge.SystemIntegrationEventDispatched += (_, _) => dispatched++;

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "unit-test-host",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-main",
            Metadata = new Dictionary<string, string>
            {
                ["platform.unknown"] = "invalid-reserved-key"
            }
        }, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, eventResult.Outcome);
        Assert.Equal("system-integration-event-metadata-key-unregistered", eventResult.DenyReason);
        Assert.Equal(0, dispatched);
        Assert.Equal(0, policy.EvaluateCalls);
    }

    [Fact]
    public void Inbound_event_timestamp_is_normalized_to_utc_millisecond_precision_before_dispatch()
    {
        var provider = new TestHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider, new AllowAllPolicy());
        var root = Guid.NewGuid();
        var dispatched = new List<WebViewSystemIntegrationEventRequest>();
        bridge.SystemIntegrationEventDispatched += (_, e) => dispatched.Add(e);
        var occurredAt = new DateTimeOffset(2026, 2, 25, 1, 2, 3, TimeSpan.Zero).AddTicks(4321);

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Source = "unit-test-host",
            OccurredAtUtc = occurredAt,
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-main",
            Metadata = new Dictionary<string, string>
            {
                ["platform.source"] = "unit-test"
            }
        }, root);

        Assert.Equal(WebViewHostCapabilityCallOutcome.Allow, eventResult.Outcome);
        var delivered = Assert.Single(dispatched);
        var expectedTicks = occurredAt.UtcTicks - (occurredAt.UtcTicks % TimeSpan.TicksPerMillisecond);
        var expected = new DateTimeOffset(expectedTicks, TimeSpan.Zero);
        Assert.Equal(expected, delivered.OccurredAtUtc);
        Assert.Equal(expected, eventResult.Value!.OccurredAtUtc);
        Assert.Equal(TimeSpan.Zero, delivered.OccurredAtUtc.Offset);
        Assert.Equal(0, delivered.OccurredAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
    }

    [Fact]
    public void Missing_core_fields_are_denied_before_policy_and_dispatch()
    {
        var provider = new TestHostCapabilityProvider();
        var policy = new CountingAllowPolicy();
        var bridge = new WebViewHostCapabilityBridge(provider, policy);
        var dispatched = 0;
        bridge.SystemIntegrationEventDispatched += (_, _) => dispatched++;

        var eventResult = bridge.DispatchSystemIntegrationEvent(new WebViewSystemIntegrationEventRequest
        {
            Kind = WebViewSystemIntegrationEventKind.TrayInteracted,
            ItemId = "tray-main",
            Metadata = new Dictionary<string, string>
            {
                ["platform.source"] = "unit-test"
            }
        }, Guid.NewGuid());

        Assert.Equal(WebViewHostCapabilityCallOutcome.Deny, eventResult.Outcome);
        Assert.Equal("system-integration-event-core-field-missing", eventResult.DenyReason);
        Assert.Equal(0, dispatched);
        Assert.Equal(0, policy.EvaluateCalls);
    }

    private sealed class AllowAllPolicy : IWebViewHostCapabilityPolicy
    {
        public WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context)
            => WebViewHostCapabilityDecision.Allow();
    }

    private sealed class DenyAllPolicy : IWebViewHostCapabilityPolicy
    {
        public WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context)
            => WebViewHostCapabilityDecision.Deny("denied-by-policy");
    }

    private sealed class ThrowingPolicy : IWebViewHostCapabilityPolicy
    {
        public WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context)
            => throw new InvalidOperationException("policy exploded");
    }

    private sealed class SelectivePolicy : IWebViewHostCapabilityPolicy
    {
        public WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context)
            => context.Operation == WebViewHostCapabilityOperation.NotificationShow
                ? WebViewHostCapabilityDecision.Deny("notification-denied")
                : WebViewHostCapabilityDecision.Allow();
    }

    private sealed class CountingAllowPolicy : IWebViewHostCapabilityPolicy
    {
        public int EvaluateCalls { get; private set; }

        public WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context)
        {
            EvaluateCalls++;
            return WebViewHostCapabilityDecision.Allow();
        }
    }

    private sealed class DenySystemIntegrationPolicy : IWebViewHostCapabilityPolicy
    {
        public WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context)
            => context.Operation is WebViewHostCapabilityOperation.MenuApplyModel
                or WebViewHostCapabilityOperation.TrayUpdateState
                or WebViewHostCapabilityOperation.SystemActionExecute
                ? WebViewHostCapabilityDecision.Deny("system-integration-denied")
                : WebViewHostCapabilityDecision.Allow();
    }

    private sealed class DenyInboundEventPolicy : IWebViewHostCapabilityPolicy
    {
        public WebViewHostCapabilityDecision Evaluate(in WebViewHostCapabilityRequestContext context)
            => context.Operation is WebViewHostCapabilityOperation.TrayInteractionEventDispatch
                or WebViewHostCapabilityOperation.MenuInteractionEventDispatch
                ? WebViewHostCapabilityDecision.Deny("inbound-event-denied")
                : WebViewHostCapabilityDecision.Allow();
    }

    private sealed class TestHostCapabilityProvider : IWebViewHostCapabilityProvider
    {
        public int CallCount { get; private set; }
        public WebViewHostCapabilityOperation? ThrowOn { get; init; }
        public List<Uri> ExternalOpens { get; } = [];

        public string? ReadClipboardText()
        {
            ThrowIfNeeded(WebViewHostCapabilityOperation.ClipboardReadText);
            CallCount++;
            return "from-clipboard";
        }

        public void WriteClipboardText(string text)
        {
            ThrowIfNeeded(WebViewHostCapabilityOperation.ClipboardWriteText);
            CallCount++;
        }

        public WebViewFileDialogResult ShowOpenFileDialog(WebViewOpenFileDialogRequest request)
        {
            ThrowIfNeeded(WebViewHostCapabilityOperation.FileDialogOpen);
            CallCount++;
            return new WebViewFileDialogResult
            {
                IsCanceled = false,
                Paths = ["C:\\temp\\open.txt"]
            };
        }

        public WebViewFileDialogResult ShowSaveFileDialog(WebViewSaveFileDialogRequest request)
        {
            ThrowIfNeeded(WebViewHostCapabilityOperation.FileDialogSave);
            CallCount++;
            return new WebViewFileDialogResult
            {
                IsCanceled = false,
                Paths = ["C:\\temp\\save.txt"]
            };
        }

        public void OpenExternal(Uri uri)
        {
            ThrowIfNeeded(WebViewHostCapabilityOperation.ExternalOpen);
            CallCount++;
            ExternalOpens.Add(uri);
        }

        public void ShowNotification(WebViewNotificationRequest request)
        {
            ThrowIfNeeded(WebViewHostCapabilityOperation.NotificationShow);
            CallCount++;
        }

        public void ApplyMenuModel(WebViewMenuModelRequest request)
        {
            ThrowIfNeeded(WebViewHostCapabilityOperation.MenuApplyModel);
            CallCount++;
        }

        public void UpdateTrayState(WebViewTrayStateRequest request)
        {
            ThrowIfNeeded(WebViewHostCapabilityOperation.TrayUpdateState);
            CallCount++;
        }

        public void ExecuteSystemAction(WebViewSystemActionRequest request)
        {
            ThrowIfNeeded(WebViewHostCapabilityOperation.SystemActionExecute);
            CallCount++;
        }

        private void ThrowIfNeeded(WebViewHostCapabilityOperation op)
        {
            if (ThrowOn == op)
                throw new InvalidOperationException($"Provider failure on {op}");
        }
    }
}
