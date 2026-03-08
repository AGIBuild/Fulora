using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agibuild.Fulora.Shell;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed partial class ShellExperienceBranchCoverageTests
{
    [Fact]
    public void NewWindow_ExternalBrowser_opens_uri_via_bridge()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var provider = new TrackingHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ExternalBrowser()),
            HostCapabilityBridge = bridge
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/external"));
        DispatcherTestPump.WaitUntil(dispatcher, () => provider.OpenExternalCalledUris.Count == 1);

        Assert.Equal(new Uri("https://example.com/external"), provider.OpenExternalCalledUris[0]);
    }

    [Fact]
    public void NewWindow_ExternalBrowser_null_uri_reports_failure()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var provider = new TrackingHostCapabilityProvider();
        var bridge = new WebViewHostCapabilityBridge(provider);
        WebViewShellPolicyErrorEventArgs? error = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ExternalBrowser()),
            HostCapabilityBridge = bridge,
            PolicyErrorHandler = (_, e) => error = e
        });

        adapter.RaiseNewWindowRequested(uri: null);
        DispatcherTestPump.WaitUntil(dispatcher, () => error is not null);

        Assert.Equal(WebViewShellPolicyDomain.ExternalOpen, error!.Domain);
    }

    [Fact]
    public void NewWindow_ExternalBrowser_without_bridge_reports_failure()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        WebViewShellPolicyErrorEventArgs? error = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ExternalBrowser()),
            PolicyErrorHandler = (_, e) => error = e
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/ext"));
        DispatcherTestPump.WaitUntil(dispatcher, () => error is not null);

        Assert.Equal(WebViewShellPolicyDomain.ExternalOpen, error!.Domain);
    }

    [Fact]
    public void NewWindow_ExternalBrowser_deny_reports_error()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var provider = new TrackingHostCapabilityProvider();
        var denyPolicy = new DenyAllCapabilityPolicy();
        var bridge = new WebViewHostCapabilityBridge(provider, denyPolicy);
        WebViewShellPolicyErrorEventArgs? error = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ExternalBrowser()),
            HostCapabilityBridge = bridge,
            PolicyErrorHandler = (_, e) => error = e
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/denied"));
        DispatcherTestPump.WaitUntil(dispatcher, () => error is not null);

        Assert.Equal(WebViewShellPolicyDomain.ExternalOpen, error!.Domain);
    }

    [Fact]
    public void NewWindow_ExternalBrowser_provider_throws_reports_failure()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var provider = new TrackingHostCapabilityProvider { ThrowOnOpenExternal = true };
        var bridge = new WebViewHostCapabilityBridge(provider);
        WebViewShellPolicyErrorEventArgs? error = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ExternalBrowser()),
            HostCapabilityBridge = bridge,
            PolicyErrorHandler = (_, e) => error = e
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/fail"));
        DispatcherTestPump.WaitUntil(dispatcher, () => error is not null);

        Assert.Equal(WebViewShellPolicyDomain.ExternalOpen, error!.Domain);
    }

    [Fact]
    public void NewWindow_Delegate_strategy_sets_handled()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        NewWindowRequestedEventArgs? observed = null;
        core.NewWindowRequested += (_, e) => observed = e;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.Delegate(handled: true))
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/delegate"));
        DispatcherTestPump.WaitUntil(dispatcher, () => observed is not null);

        Assert.True(observed!.Handled);
    }

    [Fact]
    public async Task CloseManagedWindowAsync_returns_false_when_disposed()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ManagedWindow()),
            ManagedWindowFactory = _ => new FullWebView()
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/managed"));
        DispatcherTestPump.WaitUntil(dispatcher, () => shell.ManagedWindowCount == 1);

        var windowId = shell.GetManagedWindowIds()[0];
        shell.Dispose();

        Assert.False(await shell.CloseManagedWindowAsync(windowId, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CloseManagedWindowAsync_unknown_id_returns_false()
    {
        using var webView = new FullWebView();
        using var shell = new WebViewShellExperience(webView, new WebViewShellExperienceOptions());

        Assert.False(await shell.CloseManagedWindowAsync(Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CloseManagedWindowAsync_success_lifecycle()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var lifecycleStates = new List<WebViewManagedWindowLifecycleState>();
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ManagedWindow()),
            ManagedWindowFactory = _ => new FullWebView()
        });
        shell.ManagedWindowLifecycleChanged += (_, e) => lifecycleStates.Add(e.State);

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/managed"));
        DispatcherTestPump.WaitUntil(dispatcher, () => shell.ManagedWindowCount == 1);

        var windowId = shell.GetManagedWindowIds()[0];
        var closed = await shell.CloseManagedWindowAsync(windowId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(closed);
        Assert.Equal(0, shell.ManagedWindowCount);
        Assert.Contains(WebViewManagedWindowLifecycleState.Closing, lifecycleStates);
        Assert.Contains(WebViewManagedWindowLifecycleState.Closed, lifecycleStates);
    }

    [Fact]
    public async Task CloseManagedWindowAsync_timeout_reports_cancellation()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        WebViewShellPolicyErrorEventArgs? error = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ManagedWindow()),
            ManagedWindowFactory = _ => new FullWebView(),
            ManagedWindowCloseAsync = async (_, ct) => await Task.Delay(TimeSpan.FromSeconds(30), ct),
            PolicyErrorHandler = (_, e) => error = e
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/managed"));
        DispatcherTestPump.WaitUntil(dispatcher, () => shell.ManagedWindowCount == 1);

        var windowId = shell.GetManagedWindowIds()[0];
        var closed = await shell.CloseManagedWindowAsync(windowId, timeout: TimeSpan.FromMilliseconds(10), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(closed);
        Assert.NotNull(error);
        Assert.Equal(WebViewShellPolicyDomain.ManagedWindowLifecycle, error!.Domain);
    }

    [Fact]
    public async Task CloseManagedWindowAsync_handler_exception_reports_failure()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        WebViewShellPolicyErrorEventArgs? error = null;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ManagedWindow()),
            ManagedWindowFactory = _ => new FullWebView(),
            ManagedWindowCloseAsync = (_, _) => throw new InvalidOperationException("close failed"),
            PolicyErrorHandler = (_, e) => error = e
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/managed"));
        DispatcherTestPump.WaitUntil(dispatcher, () => shell.ManagedWindowCount == 1);

        var windowId = shell.GetManagedWindowIds()[0];
        var closed = await shell.CloseManagedWindowAsync(windowId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(closed);
        Assert.NotNull(error);
    }

    [Fact]
    public void ManagedWindow_factory_returns_null_falls_back()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        NewWindowRequestedEventArgs? observed = null;
        core.NewWindowRequested += (_, e) => observed = e;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ManagedWindow()),
            ManagedWindowFactory = _ => null!
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/managed"));
        DispatcherTestPump.WaitUntil(dispatcher, () => observed is not null);

        Assert.Equal(0, shell.ManagedWindowCount);
        Assert.False(observed!.Handled);
    }

    [Fact]
    public void ManagedWindow_without_factory_falls_back()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        NewWindowRequestedEventArgs? observed = null;
        core.NewWindowRequested += (_, e) => observed = e;
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ManagedWindow())
        });

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/managed"));
        DispatcherTestPump.WaitUntil(dispatcher, () => observed is not null);

        Assert.Equal(0, shell.ManagedWindowCount);
    }

    [Fact]
    public void ManagedWindow_with_session_policy_resolves_scope()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var lifecycleStates = new List<WebViewManagedWindowLifecycleState>();
        using var shell = new WebViewShellExperience(core, new WebViewShellExperienceOptions
        {
            NewWindowPolicy = new DelegateNewWindowPolicy((_, _, _) =>
                WebViewNewWindowStrategyDecision.ManagedWindow("custom-scope")),
            ManagedWindowFactory = _ => new FullWebView(),
            SessionPolicy = new IsolatedSessionPolicy(),
            SessionContext = new WebViewShellSessionContext("tenant"),
            SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver((ctx, _) =>
                new WebViewSessionPermissionProfile
                {
                    ProfileIdentity = $"profile:{ctx.ScopeIdentity}",
                    SessionDecisionOverride = new WebViewShellSessionDecision(
                        WebViewShellSessionScope.Isolated, $"profile:{ctx.ScopeIdentity}")
                })
        });
        shell.ManagedWindowLifecycleChanged += (_, e) => lifecycleStates.Add(e.State);

        adapter.RaiseNewWindowRequested(new Uri("https://example.com/managed"));
        DispatcherTestPump.WaitUntil(dispatcher, () => shell.ManagedWindowCount == 1);

        Assert.Contains(WebViewManagedWindowLifecycleState.Created, lifecycleStates);
        Assert.Contains(WebViewManagedWindowLifecycleState.Attached, lifecycleStates);
        Assert.Contains(WebViewManagedWindowLifecycleState.Ready, lifecycleStates);
    }

    [Fact]
    public void DelegateNewWindowPolicy_null_decider_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateNewWindowPolicy(
                (Func<IWebView, NewWindowRequestedEventArgs, WebViewNewWindowPolicyContext,
                    WebViewNewWindowStrategyDecision>)null!));
    }

    [Fact]
    public void DelegateNewWindowPolicy_null_handler_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateNewWindowPolicy((Action<IWebView, NewWindowRequestedEventArgs>)null!));
    }
}
