using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.Database;

/// <summary>
/// Bridge plugin manifest for the Database service.
/// Register with: <c>bridge.UsePlugin&lt;DatabasePlugin&gt;();</c>
/// </summary>
public sealed class DatabasePlugin : IBridgePlugin
{
    /// <summary>Returns policy metadata for the Database plugin.</summary>
    public static BridgePluginMetadata GetMetadata()
        => new(
            "Agibuild.Fulora.Plugin.Database",
            ["plugin.database.read", "plugin.database.write"],
            [],
            [
                "Use parameterized SQL when forwarding untrusted input into database operations.",
                "Review database path, migrations, and backup handling before enabling production writes."
            ],
            ["desktop-hosts", "connection-options-required-for-production"]);

    /// <summary>Returns service descriptors for the Database plugin. Resolves <see cref="DatabaseOptions"/> from DI when available.</summary>
    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<IDatabaseService>(
            sp =>
            {
                var options = sp?.GetService(typeof(DatabaseOptions)) as DatabaseOptions;
                return new DatabaseService(options);
            });
    }

    /// <summary>
    /// Returns service descriptors with the given options.
    /// Use when registering manually with custom <see cref="DatabaseOptions"/>.
    /// </summary>
    public static IEnumerable<BridgePluginServiceDescriptor> GetServices(DatabaseOptions options)
    {
        yield return BridgePluginServiceDescriptor.Create<IDatabaseService>(
            _ => new DatabaseService(options));
    }
}
