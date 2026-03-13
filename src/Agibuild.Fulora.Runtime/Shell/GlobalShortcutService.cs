using System.Collections.Concurrent;
using Agibuild.Fulora.Shell;

namespace Agibuild.Fulora;

/// <summary>
/// Manages OS-level global hotkey registration with policy governance and lifecycle management.
/// Delegates actual OS calls to an <see cref="IGlobalShortcutPlatformProvider"/>.
/// </summary>
public sealed class GlobalShortcutService : IGlobalShortcutService, IDisposable
{
    private readonly IGlobalShortcutPlatformProvider _provider;
    private readonly IWebViewHostCapabilityPolicy? _policy;
    private readonly Dictionary<string, GlobalShortcutBinding> _bindings = new();
    private readonly BridgeEvent<GlobalShortcutTriggeredEvent> _shortcutTriggered = new();
    private readonly ConcurrentDictionary<string, byte> _suppressedIds = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Creates a new global shortcut service with the given platform provider and optional policy.</summary>
    public GlobalShortcutService(
        IGlobalShortcutPlatformProvider provider,
        IWebViewHostCapabilityPolicy? policy = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _policy = policy;
        _provider.ShortcutActivated += OnShortcutActivated;
    }

    /// <inheritdoc />
    public IBridgeEvent<GlobalShortcutTriggeredEvent> ShortcutTriggered => _shortcutTriggered;

    /// <inheritdoc />
    public Task<GlobalShortcutResult> Register(GlobalShortcutBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!_provider.IsSupported)
            return Task.FromResult(GlobalShortcutResult.Unsupported());

        if (_policy is not null)
        {
            var context = new WebViewHostCapabilityRequestContext(
                Guid.Empty, null, null,
                WebViewHostCapabilityOperation.GlobalShortcutRegister);
            var decision = _policy.Evaluate(context);
            if (!decision.IsAllowed)
                return Task.FromResult(GlobalShortcutResult.Denied(decision.Reason ?? "Policy denied."));
        }

        lock (_lock)
        {
            if (_bindings.ContainsKey(binding.Id))
                return Task.FromResult(GlobalShortcutResult.DuplicateId(binding.Id));

            if (!_provider.Register(binding.Id, binding.Key, binding.Modifiers))
                return Task.FromResult(GlobalShortcutResult.Conflict(
                    $"Key combination {binding.Modifiers}+{binding.Key} is already taken."));

            _bindings[binding.Id] = binding;
        }

        return Task.FromResult(GlobalShortcutResult.Success());
    }

    /// <inheritdoc />
    public Task<GlobalShortcutResult> Unregister(string shortcutId)
    {
        ArgumentNullException.ThrowIfNull(shortcutId);

        lock (_lock)
        {
            if (!_bindings.Remove(shortcutId))
                return Task.FromResult(GlobalShortcutResult.NotFound(shortcutId));

            _provider.Unregister(shortcutId);
        }

        return Task.FromResult(GlobalShortcutResult.Success());
    }

    /// <inheritdoc />
    public Task<bool> IsRegistered(string shortcutId)
    {
        lock (_lock)
        {
            return Task.FromResult(_bindings.ContainsKey(shortcutId));
        }
    }

    /// <inheritdoc />
    public Task<GlobalShortcutBinding[]> GetRegistered()
    {
        lock (_lock)
        {
            return Task.FromResult(_bindings.Values.ToArray());
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _provider.ShortcutActivated -= OnShortcutActivated;

        lock (_lock)
        {
            foreach (var id in _bindings.Keys.ToArray())
            {
                _provider.Unregister(id);
            }
            _bindings.Clear();
        }

        _provider.Dispose();
    }

    /// <summary>
    /// Suppresses the next activation of the given shortcut ID.
    /// Called by <c>WebViewShortcutRouter</c> when a window-local binding handles the same key combo.
    /// </summary>
    internal void SuppressNextActivation(string shortcutId)
    {
        _suppressedIds.TryAdd(shortcutId, 0);
    }

    /// <summary>
    /// Finds a registered shortcut matching the given key + modifiers, or null.
    /// Used by <c>WebViewShortcutRouter</c> for priority resolution.
    /// </summary>
    internal string? FindIdByChord(ShortcutKey key, ShortcutModifiers modifiers)
    {
        lock (_lock)
        {
            foreach (var kvp in _bindings)
            {
                if (kvp.Value.Key == key && kvp.Value.Modifiers == modifiers)
                    return kvp.Key;
            }
        }
        return null;
    }

    private void OnShortcutActivated(string shortcutId)
    {
        if (_suppressedIds.TryRemove(shortcutId, out _))
            return;

        lock (_lock)
        {
            if (!_bindings.ContainsKey(shortcutId))
                return;
        }

        _shortcutTriggered.Emit(new GlobalShortcutTriggeredEvent
        {
            Id = shortcutId,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
