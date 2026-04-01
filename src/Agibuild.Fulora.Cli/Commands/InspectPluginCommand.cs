using System.CommandLine;

namespace Agibuild.Fulora.Cli.Commands;

internal static class InspectPluginCommand
{
    public static Command Create()
    {
        var packageArgument = new Argument<string>("package") { Description = "Plugin package id to inspect" };
        var pluginCommand = new Command("plugin") { Description = "Inspect an installed Fulora plugin manifest" };
        pluginCommand.Arguments.Add(packageArgument);
        pluginCommand.SetAction(parseResult =>
        {
            var packageId = parseResult.GetValue(packageArgument);
            return Execute(packageId!);
        });

        var inspectCommand = new Command("inspect") { Description = "Inspect Fulora resources" };
        inspectCommand.Subcommands.Add(pluginCommand);
        return inspectCommand;
    }

    internal static int Execute(
        string packageId,
        string? cwd = null,
        Func<string, string, PluginManifest?>? findManifest = null,
        TextWriter? output = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        cwd ??= Directory.GetCurrentDirectory();
        output ??= Console.Out;
        findManifest ??= ListPluginsCommand.FindManifest;

        var plugin = ListPluginsCommand.GetProjectPlugins(cwd)
            .FirstOrDefault(candidate => string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(plugin.PackageId))
        {
            output.WriteLine($"Plugin '{packageId}' is not installed in the current project.");
            return 1;
        }

        var manifest = findManifest(plugin.PackageId, plugin.Version);
        if (manifest is null)
        {
            output.WriteLine($"Manifest for plugin '{plugin.PackageId}' was not found.");
            return 1;
        }

        output.WriteLine(FormatManifestDetails(manifest, plugin.Version));
        return 0;
    }

    internal static string FormatManifestDetails(PluginManifest manifest, string version)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var lines = new List<string>
        {
            $"{manifest.Id} ({version})",
            $"Display name: {manifest.DisplayName}",
            $"Minimum Fulora version: {manifest.MinFuloraVersion}",
            $"Required capabilities: {FormatValues(manifest.RequiredCapabilities)}",
            $"Optional capabilities: {FormatValues(manifest.OptionalCapabilities)}",
            $"Security notes: {FormatValues(manifest.SecurityNotes)}",
            $"Platform constraints: {FormatValues(manifest.PlatformConstraints)}"
        };

        if (manifest.Platforms is { Length: > 0 })
            lines.Add($"Platforms: {string.Join(", ", manifest.Platforms)}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatValues(IEnumerable<string> values)
    {
        var materialized = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return materialized.Length == 0 ? "(none)" : string.Join(", ", materialized);
    }
}
