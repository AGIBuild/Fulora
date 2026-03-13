using System.Collections.Concurrent;
using System.Text.Json;

namespace Agibuild.Fulora;

/// <summary>
/// Thread-safe shared state store with last-writer-wins (LWW) conflict resolution.
/// Each entry tracks a timestamp; stale writes are silently ignored.
/// </summary>
public sealed class SharedStateStore : ISharedStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ConcurrentDictionary<string, StateEntry> _entries = new();

    /// <inheritdoc />
    public event EventHandler<StateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public void Set(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        SetInternal(key, value, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Sets a value with an explicit timestamp for LWW conflict resolution.
    /// Used internally for replayed/remote writes.
    /// </summary>
    internal void Set(string key, string? value, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        SetInternal(key, value, timestamp);
    }

    private void SetInternal(string key, string? value, DateTimeOffset timestamp)
    {
        string? oldValue = null;
        var changed = false;

        _entries.AddOrUpdate(
            key,
            _ =>
            {
                changed = true;
                return new StateEntry(value, timestamp);
            },
            (_, existing) =>
            {
                if (timestamp < existing.Timestamp)
                    return existing;

                if (existing.Value == value)
                    return existing;

                oldValue = existing.Value;
                changed = true;
                return new StateEntry(value, timestamp);
            });

        if (changed)
            StateChanged?.Invoke(this, new StateChangedEventArgs(key, oldValue, value));
    }

    /// <inheritdoc />
    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _entries.TryGetValue(key, out var entry) ? entry.Value : null;
    }

    /// <inheritdoc />
    public bool TryGet(string key, out string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_entries.TryGetValue(key, out var entry))
        {
            value = entry.Value;
            return true;
        }
        value = null;
        return false;
    }

    /// <inheritdoc />
    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_entries.TryRemove(key, out var removed))
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs(key, removed.Value, null));
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string?> GetSnapshot()
    {
        var snapshot = new Dictionary<string, string?>();
        foreach (var kvp in _entries)
            snapshot[kvp.Key] = kvp.Value.Value;
        return snapshot;
    }

    /// <inheritdoc />
    public void Set<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        Set(key, json);
    }

    /// <inheritdoc />
    public T? Get<T>(string key)
    {
        var json = Get(key);
        if (json is null) return default;
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private sealed record StateEntry(string? Value, DateTimeOffset Timestamp);
}
