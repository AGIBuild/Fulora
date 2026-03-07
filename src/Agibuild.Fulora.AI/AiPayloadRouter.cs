namespace Agibuild.Fulora.AI;

/// <summary>
/// Routes binary payloads based on size: small payloads are Base64-encoded inline,
/// large payloads are stored in the blob store and referenced by URL.
/// </summary>
public sealed class AiPayloadRouter
{
    /// <summary>Default threshold (256 KB). Below this, inline Base64 is used.</summary>
    public const int DefaultThresholdBytes = 256 * 1024;

    private readonly IAiPayloadStore _store;
    private readonly int _thresholdBytes;

    public AiPayloadRouter(IAiPayloadStore store, int thresholdBytes = DefaultThresholdBytes)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _thresholdBytes = thresholdBytes;
    }

    /// <summary>
    /// Routes a payload. Returns either a Base64 data URI (inline) or a blob store reference URL.
    /// </summary>
    public PayloadReference Route(AiMediaPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Data.Length <= _thresholdBytes)
        {
            var base64 = Convert.ToBase64String(payload.Data);
            return new PayloadReference(
                IsInline: true,
                Value: $"data:{payload.MimeType};base64,{base64}",
                BlobId: null);
        }

        var blobId = _store.Store(payload);
        return new PayloadReference(
            IsInline: false,
            Value: $"app://ai/blob/{blobId}",
            BlobId: blobId);
    }
}

/// <summary>
/// Result of payload routing: either inline Base64 or blob store reference.
/// </summary>
public sealed record PayloadReference(bool IsInline, string Value, string? BlobId);
