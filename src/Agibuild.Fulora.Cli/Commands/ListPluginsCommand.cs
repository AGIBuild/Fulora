using System.CommandLine;
using System.Globalization;
using System.Xml.Linq;
using Agibuild.Fulora;

namespace Agibuild.Fulora.Cli.Commands;

internal static class ListPluginsCommand
{
    private const string PluginPrefix = "Agibuild.Fulora.Plugin.";

    public static Command Create()
    {
        var checkOpt = new Option<bool>("--check") { Description = "Check plugin compatibility with installed Fulora version" };

        var pluginsCommand = new Command("plugins") { Description = "List installed Fulora plugins from the project" };
        pluginsCommand.Options.Add(checkOpt);

        pluginsCommand.SetAction((parseResult) =>
        {
            var check = parseResult.GetValue(checkOpt);
            var result = Execute(check);
            return result;
        });

        var listCommand = new Command("list") { Description = "List project resources" };
        listCommand.Subcommands.Add(pluginsCommand);
        listCommand.Subcommands.Add(ListCapabilitiesCommand.Create());
        return listCommand;
    }

    internal static int Execute(bool check = false)
    {
        var cwd = Directory.GetCurrentDirectory();
        var allPlugins = GetProjectPlugins(cwd);
        if (allPlugins.Count == 0)
        {
            if (Directory.GetFiles(cwd, "*.csproj").Length == 0)
                Console.WriteLine("No .csproj file found in the current directory.");
            else
                Console.WriteLine("No Fulora plugins installed.");
            return 0;
        }

        if (check)
            return ExecuteWithCheck(allPlugins, cwd);

        var idWidth = Math.Max(6, allPlugins.Max(p => p.PackageId.Length));
        var verWidth = Math.Max(7, allPlugins.Max(p => p.Version.Length));
        var projWidth = Math.Max(7, allPlugins.Max(p => p.Project.Length));

        var headerFmt = string.Format(CultureInfo.InvariantCulture, "{0,-" + projWidth + "} {1,-" + idWidth + "} {2,-" + verWidth + "}", "Project", "Package", "Version");
        Console.WriteLine(headerFmt);
        Console.WriteLine(new string('-', projWidth + idWidth + verWidth + 4));

        var rowFmt = "{0,-" + projWidth + "} {1,-" + idWidth + "} {2,-" + verWidth + "}";
        foreach (var (project, pkgId, version) in allPlugins.OrderBy(p => p.Project).ThenBy(p => p.PackageId))
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, rowFmt, project, pkgId, version));

        return 0;
    }

    internal static int ExecuteWithCheck(
        List<(string Project, string PackageId, string Version)> plugins,
        string cwd,
        Func<string, string, PluginManifest?>? findManifest = null,
        TextWriter? output = null)
    {
        output ??= Console.Out;
        findManifest ??= FindManifest;
        var fuloraVersion = GetInstalledFuloraVersion(cwd);
        var hasIncompatible = false;

        output.WriteLine($"Fulora version: {fuloraVersion?.ToString() ?? "(unknown)"}");
        output.WriteLine();

        foreach (var (project, pkgId, version) in plugins.OrderBy(p => p.PackageId))
        {
            var manifest = findManifest(pkgId, version);
            if (manifest == null)
            {
                output.WriteLine($"  {pkgId} {version} — no manifest found");
                continue;
            }

            if (fuloraVersion != null && !manifest.IsCompatibleWith(fuloraVersion))
            {
                output.WriteLine($"  {pkgId} {version} — INCOMPATIBLE (requires >= {manifest.MinFuloraVersion})");
                hasIncompatible = true;
            }
            else
            {
                output.WriteLine($"  {pkgId} {version} — compatible (min: {manifest.MinFuloraVersion})");
            }

            foreach (var summaryLine in FormatCapabilitySummary(manifest))
                output.WriteLine($"      {summaryLine}");
        }

        return hasIncompatible ? 1 : 0;
    }

    internal static List<(string Project, string PackageId, string Version)> GetProjectPlugins(string cwd)
    {
        var csprojFiles = Directory.GetFiles(cwd, "*.csproj");
        var allPlugins = new List<(string Project, string PackageId, string Version)>();

        foreach (var csprojPath in csprojFiles)
        {
            var plugins = GetFuloraPluginsFromCsproj(csprojPath);
            var projectName = Path.GetFileNameWithoutExtension(csprojPath);
            foreach (var (pkgId, version) in plugins)
                allPlugins.Add((projectName, pkgId, version));
        }

        return allPlugins;
    }

    internal static List<PluginManifest> ResolveInstalledManifests(
        string cwd,
        Func<string, string, PluginManifest?>? findManifest = null)
    {
        findManifest ??= FindManifest;
        return GetProjectPlugins(cwd)
            .Select(plugin => findManifest(plugin.PackageId, plugin.Version))
            .Where(manifest => manifest is not null)
            .Cast<PluginManifest>()
            .ToList();
    }

    internal static IEnumerable<string> FormatCapabilitySummary(PluginManifest manifest)
    {
        if (manifest.RequiredCapabilities.Length > 0)
            yield return $"required: {string.Join(", ", manifest.RequiredCapabilities)}";

        if (manifest.OptionalCapabilities.Length > 0)
            yield return $"optional: {string.Join(", ", manifest.OptionalCapabilities)}";
    }

    internal static PluginManifest? FindManifest(string packageId, string version)
    {
        var nugetCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages",
            packageId.ToLowerInvariant(),
            version,
            "fulora-plugin.json");

        if (File.Exists(nugetCachePath))
            return PluginManifest.LoadFromFile(nugetCachePath);

        return null;
    }

    internal static Version? GetInstalledFuloraVersion(string cwd)
    {
        foreach (var csproj in Directory.GetFiles(cwd, "*.csproj"))
        {
            try
            {
                var doc = XDocument.Load(csproj);
                var fuloraRef = doc.Descendants()
                    .Where(e => e.Name.LocalName == "PackageReference")
                    .FirstOrDefault(e =>
                    {
                        var include = e.Attribute("Include")?.Value;
                        return string.Equals(include, "Agibuild.Fulora.Avalonia", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(include, "Agibuild.Fulora.Core", StringComparison.OrdinalIgnoreCase);
                    });

                if (fuloraRef == null) continue;
                var ver = fuloraRef.Attribute("Version")?.Value
                    ?? fuloraRef.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;
                if (ver != null && Version.TryParse(ver, out var parsed))
                    return parsed;
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
            {
                // Malformed or inaccessible csproj; skip and continue scanning.
            }
        }
        return null;
    }

    internal static List<(string PackageId, string Version)> GetFuloraPluginsFromCsproj(string csprojPath)
    {
        var result = new List<(string, string)>();
        if (!File.Exists(csprojPath))
            return result;

        try
        {
            var doc = XDocument.Load(csprojPath);
            var packageRefs = doc.Descendants().Where(e => e.Name.LocalName == "PackageReference");

            foreach (var pr in packageRefs)
            {
                var include = pr.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(include) || !include.StartsWith(PluginPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var version = pr.Attribute("Version")?.Value
                    ?? pr.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value
                    ?? "(unknown)";

                result.Add((include, version));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to parse {csprojPath}: {ex.Message}");
        }

        return result;
    }
}
