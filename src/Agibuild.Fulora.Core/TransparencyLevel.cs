using System.Text.Json.Serialization;

namespace Agibuild.Fulora;

/// <summary>
/// Platform transparency composition levels.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransparencyLevel>))]
public enum TransparencyLevel
{
    /// <summary>No transparency; fully opaque window.</summary>
    None,
    /// <summary>Simple transparent background (no blur).</summary>
    Transparent,
    /// <summary>Background blur effect.</summary>
    Blur,
    /// <summary>Acrylic blur (Windows DWM acrylic composition).</summary>
    AcrylicBlur,
    /// <summary>Mica material (Windows 11+).</summary>
    Mica
}
