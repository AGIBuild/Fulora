using System;
using System.Threading.Tasks;

namespace Agibuild.Fulora.Shell;

/// <summary>
/// Thin runtime façade for browser interaction governance such as DevTools and command execution.
/// </summary>
internal sealed class ShellBrowserInteractionRuntime
{
    private readonly IWebView _webView;
    private readonly WebViewShellExperienceOptions _options;
    private readonly Guid _rootWindowId;
    private readonly WebViewHostCapabilityExecutor _executor;
    private readonly Action<WebViewShellPolicyDomain, Exception> _reportPolicyFailure;

    public ShellBrowserInteractionRuntime(
        IWebView webView,
        WebViewShellExperienceOptions options,
        Guid rootWindowId,
        WebViewHostCapabilityExecutor executor,
        Action<WebViewShellPolicyDomain, Exception> reportPolicyFailure)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rootWindowId = rootWindowId;
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _reportPolicyFailure = reportPolicyFailure ?? throw new ArgumentNullException(nameof(reportPolicyFailure));
    }

    public Task<bool> OpenDevToolsAsync()
    {
        var decision = EvaluateDevToolsPolicy(WebViewShellDevToolsAction.Open);
        if (decision is null || !decision.IsAllowed)
        {
            ReportDevToolsDenied(decision?.DenyReason, WebViewShellDevToolsAction.Open);
            return Task.FromResult(false);
        }

        return ExecuteDevToolsOperation(() => _webView.OpenDevToolsAsync());
    }

    public Task<bool> CloseDevToolsAsync()
    {
        var decision = EvaluateDevToolsPolicy(WebViewShellDevToolsAction.Close);
        if (decision is null || !decision.IsAllowed)
        {
            ReportDevToolsDenied(decision?.DenyReason, WebViewShellDevToolsAction.Close);
            return Task.FromResult(false);
        }

        return ExecuteDevToolsOperation(() => _webView.CloseDevToolsAsync());
    }

    public Task<bool> IsDevToolsOpenAsync()
    {
        var decision = EvaluateDevToolsPolicy(WebViewShellDevToolsAction.Query);
        if (decision is null || !decision.IsAllowed)
        {
            ReportDevToolsDenied(decision?.DenyReason, WebViewShellDevToolsAction.Query);
            return Task.FromResult(false);
        }

        return ExecuteDevToolsQueryOperation(() => _webView.IsDevToolsOpenAsync());
    }

    public Task<bool> ExecuteCommandAsync(WebViewCommand command)
    {
        var decision = EvaluateCommandPolicy(command);
        if (decision is null || !decision.IsAllowed)
        {
            ReportCommandDenied(command, decision?.DenyReason);
            return Task.FromResult(false);
        }

        var commandManager = _webView.TryGetCommandManager();
        if (commandManager is null)
        {
            _reportPolicyFailure(
                WebViewShellPolicyDomain.Command,
                new NotSupportedException("Command manager is not available for this WebView instance."));
            return Task.FromResult(false);
        }

        return ExecuteCommandOperation(commandManager, command);
    }

    private WebViewShellDevToolsDecision? EvaluateDevToolsPolicy(WebViewShellDevToolsAction action)
    {
        if (_options.DevToolsPolicy is null)
            return WebViewShellDevToolsDecision.Allow();

        var context = new WebViewShellDevToolsPolicyContext(
            RootWindowId: _rootWindowId,
            TargetWindowId: _rootWindowId,
            Action: action);

        return _executor.ExecutePolicyDomain(
            WebViewShellPolicyDomain.DevTools,
            () => _options.DevToolsPolicy.Decide(_webView, context));
    }

    private WebViewShellCommandDecision? EvaluateCommandPolicy(WebViewCommand command)
    {
        if (_options.CommandPolicy is null)
            return WebViewShellCommandDecision.Allow();

        var context = new WebViewShellCommandPolicyContext(
            RootWindowId: _rootWindowId,
            TargetWindowId: _rootWindowId,
            Command: command);

        return _executor.ExecutePolicyDomain(
            WebViewShellPolicyDomain.Command,
            () => _options.CommandPolicy.Decide(_webView, context));
    }

    private async Task<bool> ExecuteDevToolsOperation(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _reportPolicyFailure(WebViewShellPolicyDomain.DevTools, ex);
            return false;
        }
    }

    private async Task<bool> ExecuteDevToolsQueryOperation(Func<Task<bool>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _reportPolicyFailure(WebViewShellPolicyDomain.DevTools, ex);
            return false;
        }
    }

    private void ReportDevToolsDenied(string? denyReason, WebViewShellDevToolsAction action)
    {
        _reportPolicyFailure(
            WebViewShellPolicyDomain.DevTools,
            new UnauthorizedAccessException(
                denyReason ?? $"DevTools action '{action}' was denied by shell policy."));
    }

    private async Task<bool> ExecuteCommandOperation(ICommandManager commandManager, WebViewCommand command)
    {
        try
        {
            switch (command)
            {
                case WebViewCommand.Copy:
                    await commandManager.CopyAsync().ConfigureAwait(false);
                    break;
                case WebViewCommand.Cut:
                    await commandManager.CutAsync().ConfigureAwait(false);
                    break;
                case WebViewCommand.Paste:
                    await commandManager.PasteAsync().ConfigureAwait(false);
                    break;
                case WebViewCommand.SelectAll:
                    await commandManager.SelectAllAsync().ConfigureAwait(false);
                    break;
                case WebViewCommand.Undo:
                    await commandManager.UndoAsync().ConfigureAwait(false);
                    break;
                case WebViewCommand.Redo:
                    await commandManager.RedoAsync().ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported command action.");
            }

            return true;
        }
        catch (Exception ex)
        {
            _reportPolicyFailure(WebViewShellPolicyDomain.Command, ex);
            return false;
        }
    }

    private void ReportCommandDenied(WebViewCommand command, string? denyReason)
    {
        _reportPolicyFailure(
            WebViewShellPolicyDomain.Command,
            new UnauthorizedAccessException(
                denyReason ?? $"Command '{command}' was denied by shell policy."));
    }
}
