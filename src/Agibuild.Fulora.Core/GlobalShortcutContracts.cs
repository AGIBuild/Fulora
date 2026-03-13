namespace Agibuild.Fulora;

/// <summary>
/// Binding definition for a global shortcut: a user-defined ID with a key + modifier combination.
/// </summary>
public sealed class GlobalShortcutBinding
{
    /// <summary>User-defined unique identifier for this shortcut.</summary>
    public required string Id { get; init; }

    /// <summary>Primary key for the shortcut.</summary>
    public required ShortcutKey Key { get; init; }

    /// <summary>Modifier keys (Ctrl, Alt, Shift, Meta).</summary>
    public ShortcutModifiers Modifiers { get; init; }
}

/// <summary>
/// Outcome of a global shortcut registration or unregistration operation.
/// </summary>
public enum GlobalShortcutResultStatus
{
    /// <summary>Operation completed successfully.</summary>
    Success = 0,
    /// <summary>Policy denied the operation.</summary>
    Denied = 1,
    /// <summary>Key combination conflicts with another registration.</summary>
    Conflict = 2,
    /// <summary>Platform does not support global shortcuts.</summary>
    Unsupported = 3,
    /// <summary>A shortcut with the same ID is already registered.</summary>
    DuplicateId = 4,
    /// <summary>The specified shortcut ID was not found.</summary>
    NotFound = 5
}

/// <summary>
/// Result of a global shortcut operation.
/// </summary>
public sealed class GlobalShortcutResult
{
    /// <summary>Outcome status.</summary>
    public required GlobalShortcutResultStatus Status { get; init; }

    /// <summary>Human-readable reason when the operation did not succeed.</summary>
    public string? Reason { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static GlobalShortcutResult Success() => new() { Status = GlobalShortcutResultStatus.Success };

    /// <summary>Creates a result indicating the operation was denied by policy.</summary>
    public static GlobalShortcutResult Denied(string reason) =>
        new() { Status = GlobalShortcutResultStatus.Denied, Reason = reason };

    /// <summary>Creates a result indicating a key combination conflict with an existing registration.</summary>
    public static GlobalShortcutResult Conflict(string reason) =>
        new() { Status = GlobalShortcutResultStatus.Conflict, Reason = reason };

    /// <summary>Creates a result indicating the platform does not support global shortcuts.</summary>
    public static GlobalShortcutResult Unsupported(string? reason = null) =>
        new() { Status = GlobalShortcutResultStatus.Unsupported, Reason = reason ?? "Platform does not support global shortcuts." };

    /// <summary>Creates a result indicating a shortcut with the same ID is already registered.</summary>
    public static GlobalShortcutResult DuplicateId(string id) =>
        new() { Status = GlobalShortcutResultStatus.DuplicateId, Reason = $"Shortcut with ID '{id}' is already registered." };

    /// <summary>Creates a result indicating the specified shortcut ID was not found.</summary>
    public static GlobalShortcutResult NotFound(string id) =>
        new() { Status = GlobalShortcutResultStatus.NotFound, Reason = $"Shortcut with ID '{id}' is not registered." };
}

/// <summary>
/// Event payload pushed to JS when a registered global shortcut is triggered.
/// </summary>
public sealed class GlobalShortcutTriggeredEvent
{
    /// <summary>The ID of the triggered shortcut.</summary>
    public required string Id { get; init; }

    /// <summary>UTC timestamp of activation.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}
