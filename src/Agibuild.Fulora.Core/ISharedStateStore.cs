using System.Text.Json;

namespace Agibuild.Fulora;

/// <summary>
/// Cross-WebView reactive key-value state store with last-writer-wins conflict resolution.
/// All WebViews sharing the same store instance see a consistent view of state.
/// </summary>
public interface ISharedStateStore
{
    /// <summary>Sets a raw JSON value for the given key.</summary>
    void SetValue(string key, string? value);

    /// <summary>Gets the raw JSON value for the given key, or null if not present.</summary>
    string? GetValue(string key);

    /// <summary>Tries to get the raw JSON value for the given key.</summary>
    bool TryGet(string key, out string? value);

    /// <summary>Removes a key and its value from the store.</summary>
    bool Remove(string key);

    /// <summary>Returns a snapshot of all current key-value pairs.</summary>
    IReadOnlyDictionary<string, string?> GetSnapshot();

    /// <summary>Sets a typed value (serialized to JSON).</summary>
    void SetValue<T>(string key, T value);

    /// <summary>Gets a typed value (deserialized from JSON), or default if not present.</summary>
    T? GetValue<T>(string key);

    /// <summary>Raised when a value changes in the store.</summary>
    event EventHandler<StateChange>? StateChanged;
}

/// <summary>
/// Event arguments for state change notifications.
/// </summary>
/// <param name="Key">The key that changed.</param>
/// <param name="OldValue">The previous raw JSON value (null if key was new).</param>
/// <param name="NewValue">The new raw JSON value (null if key was removed).</param>
public sealed record StateChange(string Key, string? OldValue, string? NewValue);
