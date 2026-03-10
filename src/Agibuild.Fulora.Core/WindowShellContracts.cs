using System.Text.Json.Serialization;

namespace Agibuild.Fulora;

/// <summary>
/// Requested shell-window settings from web content.
/// </summary>
public sealed class WindowShellSettings
{
    /// <summary>
    /// Theme preference: <c>"system"</c>, <c>"liquid"</c>, or <c>"classic"</c>.
    /// </summary>
    public string ThemePreference { get; init; } = "system";

    /// <summary>
    /// Whether host-level transparency is requested.
    /// </summary>
    public bool EnableTransparency { get; init; } = true;

    /// <summary>
    /// Requested glass opacity percentage in range [20, 95].
    /// </summary>
    public int GlassOpacityPercent { get; init; } = 78;
}

/// <summary>
/// Host capabilities and effective shell-window composition state.
/// </summary>
public sealed class WindowShellCapabilities
{
    /// <summary>
    /// Host platform label.
    /// </summary>
    public string Platform { get; init; } = "unknown";

    /// <summary>
    /// Whether runtime reports support for transparent composition.
    /// </summary>
    public bool SupportsTransparency { get; init; }

    /// <summary>
    /// Whether transparency is enabled in requested settings.
    /// </summary>
    public bool IsTransparencyEnabled { get; init; }

    /// <summary>
    /// Whether transparency is effectively active after host composition.
    /// </summary>
    public bool IsTransparencyEffective { get; init; }

    /// <summary>
    /// Effective transparency level as reported by host runtime.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TransparencyLevel>))]
    public TransparencyLevel EffectiveTransparencyLevel { get; init; } = TransparencyLevel.None;

    /// <summary>
    /// Validation or fallback diagnostic for current transparency state.
    /// </summary>
    public string ValidationMessage { get; init; } = "";

    /// <summary>
    /// Applied opacity percentage used by host composition.
    /// </summary>
    public int AppliedOpacityPercent { get; init; }
}

/// <summary>
/// Insets required by host chrome/safe-area layout.
/// </summary>
public sealed class WindowSafeInsets
{
    /// <summary>Top safe inset (device-independent pixels).</summary>
    public double Top { get; init; }
    /// <summary>Right safe inset (device-independent pixels).</summary>
    public double Right { get; init; }
    /// <summary>Bottom safe inset (device-independent pixels).</summary>
    public double Bottom { get; init; }
    /// <summary>Left safe inset (device-independent pixels).</summary>
    public double Left { get; init; }
}

/// <summary>
/// Host window chrome metrics relevant for web layout.
/// </summary>
public sealed class WindowChromeMetrics
{
    /// <summary>
    /// Title bar or drag-strip height reserved by host.
    /// </summary>
    public double TitleBarHeight { get; init; }

    /// <summary>
    /// Height of host drag region that initiates window move operations.
    /// </summary>
    public double DragRegionHeight { get; init; }

    /// <summary>
    /// Safe insets that web content should apply to avoid overlap with host chrome.
    /// </summary>
    public WindowSafeInsets SafeInsets { get; init; } = new();
}

/// <summary>
/// Applied shell-window state returned to web content.
/// </summary>
public sealed class WindowShellState
{
    /// <summary>
    /// Requested settings after host normalization.
    /// </summary>
    public WindowShellSettings Settings { get; init; } = new();

    /// <summary>
    /// Effective theme mode used by host shell.
    /// </summary>
    public string EffectiveThemeMode { get; init; } = "liquid";

    /// <summary>
    /// Effective host composition capability snapshot.
    /// </summary>
    public WindowShellCapabilities Capabilities { get; init; } = new();

    /// <summary>
    /// Host chrome metrics for web layout alignment.
    /// </summary>
    public WindowChromeMetrics ChromeMetrics { get; init; } = new();
}
