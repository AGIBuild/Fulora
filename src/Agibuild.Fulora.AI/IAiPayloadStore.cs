namespace Agibuild.Fulora.AI;

/// <summary>
/// In-memory store for binary payloads (images, audio, etc.) that need to be
/// transferred between JS and C# for AI operations. Supports auto-expiry.
/// </summary>
public interface IAiPayloadStore
{
    /// <summary>Stores a payload and returns a unique blob ID.</summary>
    string Store(AiMediaPayload payload, TimeSpan? ttl = null);

    /// <summary>Retrieves a payload by blob ID. Returns null if expired or not found.</summary>
    AiMediaPayload? Fetch(string blobId);

    /// <summary>Removes a payload by blob ID. Returns true if it existed.</summary>
    bool Remove(string blobId);

    /// <summary>Removes all expired blobs. Returns number of blobs evicted.</summary>
    int EvictExpired();
}
