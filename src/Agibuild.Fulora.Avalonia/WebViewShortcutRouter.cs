using Agibuild.Fulora.Shell;
using Avalonia.Input;

namespace Agibuild.Fulora;

/// <summary>
/// Action type routed from a keyboard shortcut.
/// </summary>
public enum WebViewShortcutActionKind
{
    /// <summary>Execute a standard <see cref="WebViewCommand"/> action.</summary>
    ExecuteCommand = 0,
    /// <summary>Open browser DevTools for the current WebView.</summary>
    OpenDevTools = 1
}

/// <summary>
/// Routed action payload for a keyboard shortcut.
/// </summary>
public readonly record struct WebViewShortcutAction(WebViewShortcutActionKind Kind, WebViewCommand? Command = null)
{
    /// <summary>Create a command execution action.</summary>
    public static WebViewShortcutAction ExecuteCommand(WebViewCommand command)
        => new(WebViewShortcutActionKind.ExecuteCommand, command);

    /// <summary>Create an open-DevTools action.</summary>
    public static WebViewShortcutAction OpenDevTools()
        => new(WebViewShortcutActionKind.OpenDevTools);
}

/// <summary>
/// Shortcut binding from key + modifiers to a routed action.
/// </summary>
public readonly record struct WebViewShortcutBinding(
    Key Key,
    KeyModifiers Modifiers,
    WebViewShortcutAction Action);

/// <summary>
/// Routes keyboard shortcuts to WebView command and DevTools actions.
/// When a <see cref="GlobalShortcutService"/> is provided, window-local bindings
/// take priority over global shortcuts for the same key combination.
/// </summary>
public sealed class WebViewShortcutRouter
{
    private const KeyModifiers SupportedModifierMask =
        KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta;

    private readonly IWebView _webView;
    private readonly GlobalShortcutService? _globalShortcutService;
    private readonly Dictionary<ShortcutChord, WebViewShortcutAction> _bindings = [];

    /// <summary>
    /// Creates a shortcut router with optional custom bindings and optional global shortcut service.
    /// When bindings are omitted, default shell bindings are used.
    /// </summary>
    public WebViewShortcutRouter(
        IWebView webView,
        IEnumerable<WebViewShortcutBinding>? bindings = null,
        GlobalShortcutService? globalShortcutService = null)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _globalShortcutService = globalShortcutService;

        foreach (var binding in bindings ?? CreateDefaultShellBindings())
        {
            var chord = new ShortcutChord(binding.Key, NormalizeModifiers(binding.Modifiers));
            _bindings[chord] = binding.Action;
        }
    }

    /// <summary>
    /// Creates default shell bindings for standard editing commands and DevTools.
    /// </summary>
    public static IReadOnlyList<WebViewShortcutBinding> CreateDefaultShellBindings()
    {
        var primaryModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        return
        [
            new WebViewShortcutBinding(Key.F12, KeyModifiers.None, WebViewShortcutAction.OpenDevTools()),
            new WebViewShortcutBinding(Key.I, primaryModifier | KeyModifiers.Shift, WebViewShortcutAction.OpenDevTools()),
            new WebViewShortcutBinding(Key.C, primaryModifier, WebViewShortcutAction.ExecuteCommand(WebViewCommand.Copy)),
            new WebViewShortcutBinding(Key.X, primaryModifier, WebViewShortcutAction.ExecuteCommand(WebViewCommand.Cut)),
            new WebViewShortcutBinding(Key.V, primaryModifier, WebViewShortcutAction.ExecuteCommand(WebViewCommand.Paste)),
            new WebViewShortcutBinding(Key.A, primaryModifier, WebViewShortcutAction.ExecuteCommand(WebViewCommand.SelectAll)),
            new WebViewShortcutBinding(Key.Z, primaryModifier, WebViewShortcutAction.ExecuteCommand(WebViewCommand.Undo)),
            new WebViewShortcutBinding(Key.Z, primaryModifier | KeyModifiers.Shift, WebViewShortcutAction.ExecuteCommand(WebViewCommand.Redo)),
            new WebViewShortcutBinding(Key.Y, primaryModifier, WebViewShortcutAction.ExecuteCommand(WebViewCommand.Redo))
        ];
    }

    /// <summary>
    /// Tries to execute a shortcut action from key event args.
    /// Returns true only when a shortcut is matched and execution succeeds.
    /// </summary>
    public Task<bool> TryExecuteAsync(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return TryExecuteAsync(e.Key, e.KeyModifiers);
    }

    /// <summary>
    /// Tries to execute a shortcut action from key and modifiers.
    /// Returns true only when a shortcut is matched and execution succeeds.
    /// </summary>
    public async Task<bool> TryExecuteAsync(Key key, KeyModifiers modifiers)
    {
        var normalized = NormalizeModifiers(modifiers);
        if (!_bindings.TryGetValue(new ShortcutChord(key, normalized), out var action))
            return false;

        var result = await ExecuteActionAsync(action).ConfigureAwait(false);

        if (result && _globalShortcutService is not null)
        {
            var sk = ShortcutKeyMapper.ToShortcutKey(key);
            var sm = ShortcutKeyMapper.ToShortcutModifiers(normalized);
            var globalId = _globalShortcutService.FindIdByChord(sk, sm);
            if (globalId is not null)
                _globalShortcutService.SuppressNextActivation(globalId);
        }

        return result;
    }

    private async Task<bool> ExecuteActionAsync(WebViewShortcutAction action)
    {
        switch (action.Kind)
        {
            case WebViewShortcutActionKind.OpenDevTools:
                await _webView.OpenDevToolsAsync().ConfigureAwait(false);
                return true;
            case WebViewShortcutActionKind.ExecuteCommand:
                if (action.Command is null)
                    throw new InvalidOperationException("Shortcut action requires a command value.");

                var commandManager = _webView.TryGetCommandManager();
                if (commandManager is null)
                    return false;

                await ExecuteCommandAsync(commandManager, action.Command.Value).ConfigureAwait(false);
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unsupported shortcut action.");
        }
    }

    private static Task ExecuteCommandAsync(ICommandManager commandManager, WebViewCommand command)
    {
        return command switch
        {
            WebViewCommand.Copy => commandManager.CopyAsync(),
            WebViewCommand.Cut => commandManager.CutAsync(),
            WebViewCommand.Paste => commandManager.PasteAsync(),
            WebViewCommand.SelectAll => commandManager.SelectAllAsync(),
            WebViewCommand.Undo => commandManager.UndoAsync(),
            WebViewCommand.Redo => commandManager.RedoAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported WebView command.")
        };
    }

    /// <summary>
    /// Returns whether a window-local binding exists for the given key combination.
    /// </summary>
    internal bool HasLocalBinding(Key key, KeyModifiers modifiers)
        => _bindings.ContainsKey(new ShortcutChord(key, NormalizeModifiers(modifiers)));

    private static KeyModifiers NormalizeModifiers(KeyModifiers modifiers)
        => modifiers & SupportedModifierMask;

    private readonly record struct ShortcutChord(Key Key, KeyModifiers Modifiers);
}
