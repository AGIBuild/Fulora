using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;

internal partial class BuildTask
{
    private const string LayeringGovernanceInvariantId = "GOV-041";

    private sealed record LayeringRule(
        string LayerName,
        string NamespacePrefix,
        string[] ForbiddenNamespacePrefixes);

    private static readonly LayeringRule[] LayeringGovernanceRules =
    [
        new(
            "Kernel",
            "Agibuild.Fulora.Kernel.",
            ["Agibuild.Fulora.Bridge.", "Agibuild.Fulora.Framework.", "Agibuild.Fulora.Plugin."]),
        new(
            "Bridge",
            "Agibuild.Fulora.Bridge.",
            ["Agibuild.Fulora.Framework.", "Agibuild.Fulora.Plugin."]),
        new(
            "Framework Services",
            "Agibuild.Fulora.Framework.",
            ["Agibuild.Fulora.Plugin."])
    ];

    internal Target LayeringGovernance => _ => _
        .Description("Validates docs-first layering rules and blocks forbidden reverse namespace dependencies.")
        .Executes(() =>
        {
            RunGovernanceCheck(
                "Layering governance",
                LayeringGovernanceReportFile,
                () =>
                {
                    var failures = new List<GovernanceFailure>();
                    var checks = new List<object>();
                    var guidancePath = "docs/architecture-layering.md";
                    var csFiles = Directory.GetFiles(RootDirectory, "*.cs", SearchOption.AllDirectories)
                        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    foreach (var rule in LayeringGovernanceRules)
                    {
                        var governedFiles = csFiles
                            .Where(path => File.ReadAllText(path).Contains($"namespace {rule.NamespacePrefix}", StringComparison.Ordinal))
                            .ToArray();

                        var violations = new List<object>();
                        foreach (var file in governedFiles)
                        {
                            var source = File.ReadAllText(file);
                            var relativePath = Path.GetRelativePath(RootDirectory, file).Replace('\\', '/');

                            foreach (var forbiddenPrefix in rule.ForbiddenNamespacePrefixes)
                            {
                                if (!source.Contains(forbiddenPrefix, StringComparison.Ordinal))
                                    continue;

                                violations.Add(new
                                {
                                    file = relativePath,
                                    forbiddenNamespacePrefix = forbiddenPrefix
                                });
                                failures.Add(new GovernanceFailure(
                                    Category: "layering",
                                    InvariantId: LayeringGovernanceInvariantId,
                                    SourceArtifact: relativePath,
                                    Expected: $"{rule.LayerName} code must not reference {forbiddenPrefix}; see {guidancePath}",
                                    Actual: $"forbidden namespace reference detected in {rule.NamespacePrefix} scope"));
                            }
                        }

                        checks.Add(new
                        {
                            layer = rule.LayerName,
                            namespacePrefix = rule.NamespacePrefix,
                            scannedFiles = governedFiles.Length,
                            forbiddenNamespacePrefixes = rule.ForbiddenNamespacePrefixes,
                            violations
                        });
                    }

                    var reportPayload = new
                    {
                        generatedAtUtc = DateTime.UtcNow,
                        guidance = guidancePath,
                        failureCount = failures.Count,
                        checks,
                        failures
                    };

                    return new GovernanceCheckResult(failures, reportPayload);
                });
        });
}
