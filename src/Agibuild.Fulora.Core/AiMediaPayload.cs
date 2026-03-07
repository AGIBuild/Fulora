namespace Agibuild.Fulora;

/// <summary>
/// Binary payload with MIME type metadata for multimodal AI content.
/// </summary>
public sealed record AiMediaPayload
{
    /// <summary>Raw binary data.</summary>
    public required byte[] Data { get; init; }

    /// <summary>MIME type of the data (e.g., "image/png", "audio/wav").</summary>
    public required string MimeType { get; init; }

    /// <summary>Optional display name or filename.</summary>
    public string? Name { get; init; }
}
