using Agibuild.Fulora.Cli.Commands;
using Xunit;

namespace Agibuild.Fulora.UnitTests.Cli;

public sealed class PluginCapabilityCliTests
{
    [Fact]
    public void PluginManifest_parse_reads_capability_metadata()
    {
        var manifest = PluginManifest.Parse(
            """
            {
              "id": "Agibuild.Fulora.Plugin.Database",
              "displayName": "Database",
              "services": ["DatabaseService"],
              "npmPackage": "@agibuild/bridge-plugin-database",
              "minFuloraVersion": "1.0.0",
              "platforms": ["windows", "macos"],
              "requiredCapabilities": ["plugin.database.read", "plugin.database.write"],
              "optionalCapabilities": ["plugin.database.migrate"],
              "securityNotes": ["Use parameterized SQL."],
              "platformConstraints": ["desktop-hosts"]
            }
            """);

        Assert.NotNull(manifest);
        Assert.Equal(["plugin.database.read", "plugin.database.write"], manifest!.RequiredCapabilities);
        Assert.Equal(["plugin.database.migrate"], manifest.OptionalCapabilities);
        Assert.Equal(["Use parameterized SQL."], manifest.SecurityNotes);
        Assert.Equal(["desktop-hosts"], manifest.PlatformConstraints);
    }

    [Fact]
    public void ListCapabilitiesCommand_collects_sorted_unique_capabilities()
    {
        var manifests = new[]
        {
            new PluginManifest
            {
                Id = "Agibuild.Fulora.Plugin.Database",
                RequiredCapabilities = ["plugin.database.read", "plugin.database.write"],
                OptionalCapabilities = ["plugin.database.migrate"]
            },
            new PluginManifest
            {
                Id = "Agibuild.Fulora.Plugin.FileSystem",
                RequiredCapabilities = ["plugin.filesystem.read", "plugin.filesystem.write"],
                OptionalCapabilities = ["plugin.filesystem.pick"]
            }
        };

        var capabilities = ListCapabilitiesCommand.GetCapabilities(manifests);

        Assert.Equal(
            [
                "plugin.database.migrate",
                "plugin.database.read",
                "plugin.database.write",
                "plugin.filesystem.pick",
                "plugin.filesystem.read",
                "plugin.filesystem.write"
            ],
            capabilities);
    }

    [Fact]
    public void InspectPluginCommand_formats_manifest_details_with_capabilities()
    {
        var manifest = new PluginManifest
        {
            Id = "Agibuild.Fulora.Plugin.HttpClient",
            DisplayName = "HTTP Client",
            MinFuloraVersion = "1.0.0",
            RequiredCapabilities = ["plugin.http.outbound"],
            SecurityNotes = ["Restrict outbound hosts with policy."],
            PlatformConstraints = ["desktop-hosts", "policy-governed-network-egress"]
        };

        var details = InspectPluginCommand.FormatManifestDetails(manifest, "2.0.0");

        Assert.Contains("Agibuild.Fulora.Plugin.HttpClient", details);
        Assert.Contains("Required capabilities", details);
        Assert.Contains("plugin.http.outbound", details);
        Assert.Contains("Platform constraints", details);
        Assert.Contains("policy-governed-network-egress", details);
    }

    [Fact]
    public void ListPluginsCommand_execute_with_check_includes_capability_summary()
    {
        var plugins = new List<(string Project, string PackageId, string Version)>
        {
            ("Desktop", "Agibuild.Fulora.Plugin.Database", "1.2.3")
        };

        var manifest = new PluginManifest
        {
            Id = "Agibuild.Fulora.Plugin.Database",
            MinFuloraVersion = "1.0.0",
            RequiredCapabilities = ["plugin.database.read", "plugin.database.write"],
            OptionalCapabilities = ["plugin.database.migrate"]
        };

        using var output = new StringWriter();
        var exitCode = ListPluginsCommand.ExecuteWithCheck(
            plugins,
            cwd: Path.GetTempPath(),
            findManifest: (_, _) => manifest,
            output: output);

        Assert.Equal(0, exitCode);

        var text = output.ToString();
        Assert.Contains("required: plugin.database.read, plugin.database.write", text);
        Assert.Contains("optional: plugin.database.migrate", text);
    }
}
