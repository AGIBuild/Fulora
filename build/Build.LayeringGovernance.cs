using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    private const string LayeringGovernanceInvariantId = "GOV-037";
    private static readonly Regex ProjectReferencePattern = new(
        "<ProjectReference\\s+Include=\"(?<path>[^\"]+)\"",
        RegexOptions.Compiled);

    private sealed record LayeringRule(
        string Layer,
        string[] Roots,
        string[] ForbiddenProjectReferenceMarkers,
        Regex[] ForbiddenNamespacePatterns);

    private static readonly string[] FrameworkReferenceMarkers =
    [
        "Agibuild.Fulora.Avalonia",
        "Agibuild.Fulora.Adapters.Windows",
        "Agibuild.Fulora.Adapters.Gtk",
        "Agibuild.Fulora.Adapters.MacOS",
        "Agibuild.Fulora.Adapters.Android",
        "Agibuild.Fulora.Adapters.iOS"
    ];

    private static readonly LayeringRule[] LayeringRules =
    [
        new(
            Layer: "Kernel",
            Roots:
            [
                "src/Agibuild.Fulora.Core",
                "src/Agibuild.Fulora.Adapters.Abstractions",
                "src/Agibuild.Fulora.Runtime",
                "src/Agibuild.Fulora.DependencyInjection"
            ],
            ForbiddenProjectReferenceMarkers: FrameworkReferenceMarkers.Concat(["Agibuild.Fulora.Plugin."]).ToArray(),
            ForbiddenNamespacePatterns: FrameworkReferenceMarkers
                .Select(CreateNamespacePattern)
                .Concat([CreateNamespacePattern("Agibuild.Fulora.Plugin.")])
                .ToArray()),
        new(
            Layer: "Bridge",
            Roots: ["src/Agibuild.Fulora.Bridge.Generator"],
            ForbiddenProjectReferenceMarkers: FrameworkReferenceMarkers.Concat(["Agibuild.Fulora.Plugin."]).ToArray(),
            ForbiddenNamespacePatterns: FrameworkReferenceMarkers
                .Select(CreateNamespacePattern)
                .Concat([CreateNamespacePattern("Agibuild.Fulora.Plugin.")])
                .ToArray()),
        new(
            Layer: "Framework",
            Roots:
            [
                "src/Agibuild.Fulora.Avalonia",
                "src/Agibuild.Fulora.Adapters.Windows",
                "src/Agibuild.Fulora.Adapters.Gtk",
                "src/Agibuild.Fulora.Adapters.MacOS",
                "src/Agibuild.Fulora.Adapters.Android",
                "src/Agibuild.Fulora.Adapters.iOS"
            ],
            ForbiddenProjectReferenceMarkers: ["Agibuild.Fulora.Plugin."],
            ForbiddenNamespacePatterns: [CreateNamespacePattern("Agibuild.Fulora.Plugin.")]),
        new(
            Layer: "Plugin",
            Roots: ["plugins"],
            ForbiddenProjectReferenceMarkers: FrameworkReferenceMarkers.Concat(["Agibuild.Fulora.Plugin."]).ToArray(),
            ForbiddenNamespacePatterns: FrameworkReferenceMarkers
                .Select(CreateNamespacePattern)
                .Concat([CreateNamespacePattern("Agibuild.Fulora.Plugin.")])
                .ToArray())
    ];

    internal Target LayeringGovernance => _ => _
        .Description("Blocks forbidden reverse dependencies across Kernel, Bridge, Framework, and Plugin layers.")
        .Executes(() =>
        {
            RunGovernanceCheck(
                "Layering governance",
                LayeringGovernanceReportFile,
                () =>
                {
                    var failures = new List<GovernanceFailure>();

                    foreach (var rule in LayeringRules)
                    {
                        foreach (var root in rule.Roots)
                        {
                            var fullRoot = RootDirectory / root;
                            if (!Directory.Exists(fullRoot))
                                continue;

                            foreach (var projectFile in Directory.GetFiles(fullRoot, "*.csproj", SearchOption.AllDirectories))
                            {
                                var source = File.ReadAllText(projectFile);
                                var projectReferences = ProjectReferencePattern.Matches(source)
                                    .Select(match => match.Groups["path"].Value)
                                    .ToArray();

                                foreach (var marker in rule.ForbiddenProjectReferenceMarkers)
                                {
                                    foreach (var projectReference in projectReferences)
                                    {
                                        if (!projectReference.Contains(marker, StringComparison.Ordinal))
                                            continue;

                                        failures.Add(new GovernanceFailure(
                                            Category: "layering",
                                            InvariantId: LayeringGovernanceInvariantId,
                                            SourceArtifact: ToRepoRelativePath(projectFile),
                                            Expected: $"{rule.Layer} layer avoids project references to '{marker}'",
                                            Actual: $"found project reference '{projectReference}'. See docs/architecture-layering.md"));
                                    }
                                }
                            }

                            foreach (var codeFile in Directory.GetFiles(fullRoot, "*.cs", SearchOption.AllDirectories))
                            {
                                var source = File.ReadAllText(codeFile);
                                foreach (var pattern in rule.ForbiddenNamespacePatterns)
                                {
                                    var match = pattern.Match(source);
                                    if (!match.Success)
                                        continue;

                                    failures.Add(new GovernanceFailure(
                                        Category: "layering",
                                        InvariantId: LayeringGovernanceInvariantId,
                                        SourceArtifact: ToRepoRelativePath(codeFile),
                                        Expected: $"{rule.Layer} layer avoids namespace imports outside docs/architecture-layering.md",
                                        Actual: $"found forbidden namespace import '{match.Groups["namespace"].Value}'."));
                                }
                            }
                        }
                    }

                    var reportPayload = new
                    {
                        schemaVersion = 1,
                        documentation = "docs/architecture-layering.md",
                        failureCount = failures.Count,
                        rules = LayeringRules.Select(rule => new
                        {
                            layer = rule.Layer,
                            roots = rule.Roots
                        }),
                        failures
                    };

                    return new GovernanceCheckResult(failures, reportPayload);
                });
        });

    private static Regex CreateNamespacePattern(string namespacePrefix)
    {
        var normalized = Regex.Escape(namespacePrefix.TrimEnd('.'));
        return new Regex(
            $@"(?:^|\s)(?:global\s+)?using\s+(?<namespace>{normalized}(?:\.[A-Za-z0-9_]+)*)\s*;",
            RegexOptions.Multiline | RegexOptions.Compiled);
    }

    private static string ToRepoRelativePath(string path) =>
        Path.GetRelativePath(RootDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
}
