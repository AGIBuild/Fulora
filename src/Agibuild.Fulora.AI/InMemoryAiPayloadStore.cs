using System.Collections.Concurrent;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Default in-memory implementation of <see cref="IAiPayloadStore"/> with TTL-based expiry.
/// </summary>
public sealed class InMemoryAiPayloadStore : IAiPayloadStore, IDisposable
{
    /// <summary>Default blob TTL (5 minutes).</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, BlobEntry> _store = new();
    private readonly Timer _evictionTimer;

    public InMemoryAiPayloadStore()
    {
        _evictionTimer = new Timer(_ => EvictExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public string Store(AiMediaPayload payload, TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var id = Guid.NewGuid().ToString("N");
        var expiresAt = DateTime.UtcNow + (ttl ?? DefaultTtl);
        _store[id] = new BlobEntry(payload, expiresAt);
        return id;
    }

    public AiMediaPayload? Fetch(string blobId)
    {
        if (!_store.TryGetValue(blobId, out var entry))
            return null;

        if (DateTime.UtcNow > entry.ExpiresAt)
        {
            _store.TryRemove(blobId, out _);
            return null;
        }

        return entry.Payload;
    }

    public bool Remove(string blobId) => _store.TryRemove(blobId, out _);

    public int EvictExpired()
    {
        var now = DateTime.UtcNow;
        var evicted = 0;
        foreach (var kvp in _store)
        {
            if (now > kvp.Value.ExpiresAt && _store.TryRemove(kvp.Key, out _))
                evicted++;
        }
        return evicted;
    }

    public void Dispose() => _evictionTimer.Dispose();

    private sealed record BlobEntry(AiMediaPayload Payload, DateTime ExpiresAt);
}
