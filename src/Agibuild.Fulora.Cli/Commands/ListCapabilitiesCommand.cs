using System.CommandLine;

namespace Agibuild.Fulora.Cli.Commands;

internal static class ListCapabilitiesCommand
{
    public static Command Create()
    {
        var command = new Command("capabilities") { Description = "List capabilities declared by installed Fulora plugins" };
        command.SetAction(_ => Execute());
        return command;
    }

    internal static int Execute(
        string? cwd = null,
        Func<string, string, PluginManifest?>? findManifest = null,
        TextWriter? output = null)
    {
        cwd ??= Directory.GetCurrentDirectory();
        output ??= Console.Out;

        var manifests = ListPluginsCommand.ResolveInstalledManifests(cwd, findManifest);
        if (manifests.Count == 0)
        {
            output.WriteLine("No plugin capability metadata found.");
            return 0;
        }

        foreach (var capability in GetCapabilities(manifests))
            output.WriteLine(capability);

        return 0;
    }

    internal static IReadOnlyList<string> GetCapabilities(IEnumerable<PluginManifest> manifests)
        => manifests
            .SelectMany(manifest => manifest.RequiredCapabilities.Concat(manifest.OptionalCapabilities))
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();
}
