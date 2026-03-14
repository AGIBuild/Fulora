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
        return listCommand;
    }

    internal static int Execute(bool check = false)
    {
        var cwd = Directory.GetCurrentDirectory();
        var csprojFiles = Directory.GetFiles(cwd, "*.csproj");

        if (csprojFiles.Length == 0)
        {
            Console.WriteLine("No .csproj file found in the current directory.");
            return 0;
        }

        var allPlugins = new List<(string Project, string PackageId, string Version)>();

        foreach (var csprojPath in csprojFiles)
        {
            var plugins = GetFuloraPluginsFromCsproj(csprojPath);
            var projectName = Path.GetFileNameWithoutExtension(csprojPath);
            foreach (var (pkgId, version) in plugins)
                allPlugins.Add((projectName, pkgId, version));
        }

        if (allPlugins.Count == 0)
        {
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
        List<(string Project, string PackageId, string Version)> plugins, string cwd)
    {
        var fuloraVersion = GetInstalledFuloraVersion(cwd);
        var hasIncompatible = false;

        Console.WriteLine($"Fulora version: {fuloraVersion?.ToString() ?? "(unknown)"}");
        Console.WriteLine();

        foreach (var (project, pkgId, version) in plugins.OrderBy(p => p.PackageId))
        {
            var manifest = FindManifest(pkgId, version);
            if (manifest == null)
            {
                Console.WriteLine($"  {pkgId} {version} — no manifest found");
                continue;
            }

            if (fuloraVersion != null && !manifest.IsCompatibleWith(fuloraVersion))
            {
                Console.WriteLine($"  {pkgId} {version} — INCOMPATIBLE (requires >= {manifest.MinFuloraVersion})");
                hasIncompatible = true;
            }
            else
            {
                Console.WriteLine($"  {pkgId} {version} — compatible (min: {manifest.MinFuloraVersion})");
            }
        }

        return hasIncompatible ? 1 : 0;
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
            catch { }
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
