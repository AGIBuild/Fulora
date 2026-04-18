using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Integration tests for the ICommandManager feature.
/// Exercises the full WebDialog → WebViewCore → ICommandAdapter stack.
///
/// HOW IT WORKS (for newcomers):
///   1. We create a MockWebViewAdapterWithCommands — a fake "browser" that records every command.
///   2. We wrap it in a WebDialog (the same wrapper real apps use).
///   3. We call TryGetCommandManager() to get the command interface.
///   4. We call Copy(), Cut(), etc. and check what the fake browser recorded.
///   No real browser is needed — everything runs in-memory, headless.
/// </summary>
public sealed class CommandManagerIntegrationTests
{
    private readonly TestDispatcher _dispatcher = new();

    /// <summary>Helper: creates a WebDialog backed by a command-capable mock adapter.</summary>
    private (WebDialog Dialog, MockWebViewAdapterWithCommands Adapter) CreateDialogWithCommands()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithCommands();
        var dialog = new WebDialog(host, adapter, _dispatcher);
        return (dialog, adapter);
    }

    // ──────────────────── Test 1: Feature detection ────────────────────

    [AvaloniaFact]
    public void CommandManager_available_when_adapter_supports_commands()
    {
        // Arrange: adapter implements ICommandAdapter
        var (dialog, _) = CreateDialogWithCommands();

        // Act
        var mgr = dialog.TryGetCommandManager();

        // Assert: should return a usable command manager
        Assert.NotNull(mgr);
        dialog.Dispose();
    }

    // ──────────────────── Test 2: Single command delegation ────────────────────

    [AvaloniaFact]
    public async Task Copy_delegates_to_adapter()
    {
        // Arrange
        var (dialog, adapter) = CreateDialogWithCommands();
        var mgr = dialog.TryGetCommandManager()!;

        // Act: user presses Ctrl+C
        await mgr.CopyAsync();

        // Assert: adapter received the Copy command
        Assert.Single(adapter.ExecutedCommands);
        Assert.Equal(WebViewCommand.Copy, adapter.ExecutedCommands[0]);
        dialog.Dispose();
    }

    // ──────────────────── Test 3: Command ordering ────────────────────

    [AvaloniaFact]
    public async Task SelectAll_then_Cut_executes_in_order()
    {
        var (dialog, adapter) = CreateDialogWithCommands();
        var mgr = dialog.TryGetCommandManager()!;

        // Act: user selects all, then cuts
        await mgr.SelectAllAsync();
        await mgr.CutAsync();

        // Assert: commands arrive in order
        Assert.Equal(2, adapter.ExecutedCommands.Count);
        Assert.Equal(WebViewCommand.SelectAll, adapter.ExecutedCommands[0]);
        Assert.Equal(WebViewCommand.Cut, adapter.ExecutedCommands[1]);
        dialog.Dispose();
    }

    // ──────────────────── Test 4: All six commands ────────────────────

    [AvaloniaFact]
    public async Task All_six_commands_work()
    {
        var (dialog, adapter) = CreateDialogWithCommands();
        var mgr = dialog.TryGetCommandManager()!;

        // Act: fire all six editing commands
        await mgr.CopyAsync();
        await mgr.CutAsync();
        await mgr.PasteAsync();
        await mgr.SelectAllAsync();
        await mgr.UndoAsync();
        await mgr.RedoAsync();

        // Assert: all six recorded in the correct order
        var expected = new[]
        {
            WebViewCommand.Copy,
            WebViewCommand.Cut,
            WebViewCommand.Paste,
            WebViewCommand.SelectAll,
            WebViewCommand.Undo,
            WebViewCommand.Redo
        };

        Assert.Equal(expected, adapter.ExecutedCommands);
        dialog.Dispose();
    }
}
