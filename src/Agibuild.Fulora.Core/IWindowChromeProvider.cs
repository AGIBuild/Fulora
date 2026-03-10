using System;
using System.Threading.Tasks;

namespace Agibuild.Fulora;

/// <summary>
/// Request payload for applying window appearance changes.
/// </summary>
public sealed class WindowAppearanceRequest
{
    /// <summary>Whether to enable window transparency composition.</summary>
    public bool EnableTransparency { get; init; }

    /// <summary>Requested glass opacity (20–95).</summary>
    public int OpacityPercent { get; init; }

    /// <summary>Resolved theme mode to apply ("liquid" or "classic").</summary>
    public string EffectiveThemeMode { get; init; } = "liquid";
}

/// <summary>
/// Effective transparency state as resolved by the platform provider.
/// </summary>
public sealed class TransparencyEffectiveState
{
    /// <summary>Whether transparency was requested.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Whether the platform actually applied a transparency effect.</summary>
    public bool IsEffective { get; init; }

    /// <summary>Effective composition level reported by the platform.</summary>
    public TransparencyLevel Level { get; init; }

    /// <summary>Actual opacity percentage applied to the window.</summary>
    public int AppliedOpacityPercent { get; init; }

    /// <summary>Human-readable diagnostic message about the transparency state.</summary>
    public string? ValidationMessage { get; init; }
}

/// <summary>
/// Abstracts platform-specific window chrome operations (transparency, metrics, appearance).
/// Concrete implementations (e.g. Avalonia) add window tracking on the concrete type.
/// </summary>
public interface IWindowChromeProvider
{
    /// <summary>Platform identifier (e.g. "Windows", "macOS", "Linux").</summary>
    string Platform { get; }

    /// <summary>Whether the current platform supports any transparency composition.</summary>
    bool SupportsTransparency { get; }

    /// <summary>Apply appearance settings to all managed windows.</summary>
    Task ApplyWindowAppearanceAsync(WindowAppearanceRequest request);

    /// <summary>Return the effective transparency state from the primary window.</summary>
    TransparencyEffectiveState GetTransparencyState();

    /// <summary>Return chrome layout metrics (title bar height, safe insets, etc.).</summary>
    WindowChromeMetrics GetChromeMetrics();

    /// <summary>Raised when the platform theme or transparency state changes externally.</summary>
    event EventHandler? AppearanceChanged;
}
