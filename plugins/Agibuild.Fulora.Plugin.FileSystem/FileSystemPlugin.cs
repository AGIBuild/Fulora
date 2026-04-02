using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.FileSystem;

/// <summary>
/// Bridge plugin manifest for the FileSystem service.
/// Register with: <c>bridge.UsePlugin&lt;FileSystemPlugin&gt;();</c>
/// </summary>
public sealed class FileSystemPlugin : IBridgePlugin
{
    /// <summary>Returns policy metadata for the FileSystem plugin.</summary>
    public static BridgePluginMetadata GetMetadata()
        => new(
            "Agibuild.Fulora.Plugin.FileSystem",
            ["plugin.filesystem.read", "plugin.filesystem.write"],
            ["plugin.filesystem.pick"],
            [
                "Constrain access to sandboxed or policy-approved roots.",
                "Write operations remain gated by FileSystemOptions.AllowWrite."
            ],
            ["desktop-hosts", "sandbox-or-policy-governed-paths"]);

    /// <summary>Returns the service descriptors for the FileSystem plugin.</summary>
    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<IFileSystemService>(
            _ => new FileSystemService());
    }
}
