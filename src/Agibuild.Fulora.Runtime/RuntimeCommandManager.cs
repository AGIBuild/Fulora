using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora;

/// <summary>
/// Runtime wrapper around <see cref="ICommandAdapter"/> that delegates editing commands through
/// the shared <see cref="WebViewCoreContext"/>.
/// </summary>
internal sealed class RuntimeCommandManager : ICommandManager
{
    private readonly ICommandAdapter _commandAdapter;
    private readonly WebViewCoreContext _context;

    public RuntimeCommandManager(ICommandAdapter commandAdapter, WebViewCoreContext context)
    {
        _commandAdapter = commandAdapter ?? throw new ArgumentNullException(nameof(commandAdapter));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task CopyAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Copy));

    public Task CutAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Cut));

    public Task PasteAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Paste));

    public Task SelectAllAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.SelectAll));

    public Task UndoAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Undo));

    public Task RedoAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Redo));

    private static Task ToVoidTask(Task<object?> task) => task.ContinueWith(t =>
    {
        if (t.IsFaulted)
            throw t.Exception!.GetBaseException();
    }, TaskContinuationOptions.ExecuteSynchronously);

    private Task<object?> ExecuteAsync(WebViewCommand command)
    {
        return _context.Operations.EnqueueAsync($"Command.{command}", () =>
        {
            _commandAdapter.ExecuteCommand(command);
            return Task.CompletedTask;
        });
    }
}
