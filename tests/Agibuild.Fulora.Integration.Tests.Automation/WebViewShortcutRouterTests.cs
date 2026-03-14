using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agibuild.Fulora;
using Avalonia.Input;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

public sealed class WebViewShortcutRouterTests
{
    [Fact]
    public void Default_shell_bindings_include_expected_command_and_devtools_actions()
    {
        var bindings = WebViewShortcutRouter.CreateDefaultShellBindings();

        var commandActions = bindings
            .Where(x => x.Action.Kind == WebViewShortcutActionKind.ExecuteCommand && x.Action.Command is not null)
            .Select(x => x.Action.Command!.Value)
            .ToHashSet();

        Assert.Contains(WebViewCommand.Copy, commandActions);
        Assert.Contains(WebViewCommand.Cut, commandActions);
        Assert.Contains(WebViewCommand.Paste, commandActions);
        Assert.Contains(WebViewCommand.SelectAll, commandActions);
        Assert.Contains(WebViewCommand.Undo, commandActions);
        Assert.Contains(WebViewCommand.Redo, commandActions);
        Assert.Contains(bindings, x => x.Action.Kind == WebViewShortcutActionKind.OpenDevTools);
    }

    [Fact]
    public async Task Default_shell_shortcut_executes_copy_on_platform_primary_modifier()
    {
        var commandManager = new TrackingCommandManager();
        using var webView = new TrackingWebView { CommandManager = commandManager };
        var router = new WebViewShortcutRouter(webView);
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        var handled = await router.TryExecuteAsync(Key.C, primary);

        Assert.True(handled);
        Assert.Equal([WebViewCommand.Copy], commandManager.ExecutedCommands);
    }

    [Fact]
    public async Task Default_shell_shortcut_executes_open_devtools()
    {
        using var webView = new TrackingWebView();
        var router = new WebViewShortcutRouter(webView);

        var handled = await router.TryExecuteAsync(Key.F12, KeyModifiers.None);

        Assert.True(handled);
        Assert.Equal(1, webView.OpenDevToolsCallCount);
    }

    [Fact]
    public async Task Command_shortcut_returns_false_when_command_manager_is_unavailable()
    {
        using var webView = new TrackingWebView();
        var router = new WebViewShortcutRouter(webView);
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        var handled = await router.TryExecuteAsync(Key.V, primary);

        Assert.False(handled);
    }

    [Fact]
    public async Task Unmapped_shortcut_returns_false()
    {
        using var webView = new TrackingWebView();
        var router = new WebViewShortcutRouter(webView);

        var handled = await router.TryExecuteAsync(Key.F5, KeyModifiers.None);

        Assert.False(handled);
    }

    private sealed class TrackingWebView : IWebView
    {
        public Uri Source { get; set; } = new("about:blank");
        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public bool IsLoading => false;
        public Guid ChannelId { get; } = Guid.NewGuid();

        public ICommandManager? CommandManager { get; init; }
        public int OpenDevToolsCallCount { get; private set; }

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
        public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
            => Task.FromException<FindInPageEventArgs>(new NotSupportedException());
        public Task StopFindInPageAsync(bool clearHighlights = true) => Task.CompletedTask;
        public Task<string> AddPreloadScriptAsync(string javaScript) => Task.FromException<string>(new NotSupportedException());
        public Task RemovePreloadScriptAsync(string scriptId) => Task.FromException(new NotSupportedException());
        public Task OpenDevToolsAsync()
        {
            OpenDevToolsCallCount++;
            return Task.CompletedTask;
        }

        public Task CloseDevToolsAsync() => Task.CompletedTask;
        public Task<bool> IsDevToolsOpenAsync() => Task.FromResult(false);
        public void Dispose() { }
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
