namespace Agibuild.Fulora;

/// <summary>
/// Metadata that marks an explicit exception scope for profile-level customization.
/// This metadata is consumed by governance and diagnostics to track approved escape-hatch usage.
/// </summary>
public sealed class SpaBootstrapProfileExceptionScope
{
    /// <summary>
    /// Stable identifier for the exception scope (for example: "sample-ai-chat-direct-streaming").
    /// </summary>
    public required string ScopeId { get; init; }

    /// <summary>
    /// Optional human-readable justification for using this exception scope.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Represents an explicit profile extension hook applied during bridge configuration.
/// Extensions are evaluated in declared order.
/// </summary>
public sealed class SpaBootstrapProfileExtension
{
    /// <summary>
    /// Stable extension identifier for diagnostics and governance.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Extension callback that can configure bridge behavior in a controlled way.
    /// </summary>
    public required Action<IBridgeService, IServiceProvider?, SpaBootstrapProfileExceptionScope?> Configure { get; init; }
}

/// <summary>
/// Represents a lifecycle teardown callback owned by profile bootstrap.
/// Teardowns execute in declaration order when the host adapter is destroyed.
/// </summary>
public sealed class SpaBootstrapProfileTeardown
{
    /// <summary>
    /// Stable teardown identifier for diagnostics and governance.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Teardown callback that releases profile-owned resources.
    /// </summary>
    public required Action<IServiceProvider?, SpaBootstrapProfileExceptionScope?> Execute { get; init; }
}

/// <summary>
/// Profile-level bootstrap options that wrap <see cref="SpaBootstrapOptions"/>
/// and add explicit extension hooks plus governance metadata.
/// </summary>
public sealed class SpaBootstrapProfileOptions
{
    /// <summary>
    /// The underlying SPA bootstrap options used for navigation and baseline bridge registration.
    /// </summary>
    public required SpaBootstrapOptions BootstrapOptions { get; init; }

    /// <summary>
    /// Optional ordered extension hooks for advanced profile customization.
    /// </summary>
    public IReadOnlyList<SpaBootstrapProfileExtension> Extensions { get; init; } = [];

    /// <summary>
    /// Optional governance metadata describing why an exception scope is used.
    /// </summary>
    public SpaBootstrapProfileExceptionScope? ExceptionScope { get; init; }

    /// <summary>
    /// Optional ordered teardown callbacks executed once during host disposal.
    /// </summary>
    public IReadOnlyList<SpaBootstrapProfileTeardown> Teardowns { get; init; } = [];
}
