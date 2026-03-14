using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.LocalStorage;

/// <summary>
/// Bridge service for persistent key-value storage.
/// Provides simple CRUD operations backed by a local JSON file.
/// </summary>
[JsExport]
public interface ILocalStorageService
{
    /// <summary>Gets the value for a key, or null if not found.</summary>
    Task<string?> GetValue(string key);

    /// <summary>Sets a key-value pair. Overwrites if the key exists.</summary>
    Task SetValue(string key, string value);

    /// <summary>Removes a key. No-op if the key does not exist.</summary>
    Task Remove(string key);

    /// <summary>Removes all key-value pairs.</summary>
    Task Clear();

    /// <summary>Returns all stored keys.</summary>
    Task<string[]> GetKeys();
}
