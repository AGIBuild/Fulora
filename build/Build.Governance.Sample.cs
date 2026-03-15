using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nuke.Common;

internal partial class BuildTask
{
    private static readonly string[] SampleTemplateGovernedRootsReportNames = ["samples", "templates"];

    internal Target SampleTemplatePackageReferenceGovernance => _ => _
        .Description("Enforces package-only Agibuild.Fulora references in sample/template projects.")
        .Executes(() =>
        {
            RunGovernanceCheck(
                "Sample/template package-reference governance",
                SampleTemplatePackageReferenceGovernanceReportFile,
                () =>
                {
                    var failures = new List<GovernanceFailure>();
                    var checks = new List<object>();
                    var sourceProjectReferencePattern = new Regex(
                        @"<(ProjectReference|Import)\b[^>]*(Include|Project)\s*=\s*""(?<path>[^""]*src[\\/]+Agibuild\.Fulora[^""]*)""[^>]*>",
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    var conditionalProjectReferencePattern = new Regex(
                        @"<ProjectReference\b[^>]*\bCondition\s*=",
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    var packageReferenceWithVersionAttributePattern = new Regex(
                        @"<PackageReference\b[^>]*Include=""(?<id>Agibuild\.Fulora(?:\.[^""]+)?)""[^>]*Version=""(?<version>[^""]+)""[^>]*/?>",
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    var packageReferenceElementPattern = new Regex(
                        @"<PackageReference\b[^>]*Include=""(?<id>Agibuild\.Fulora(?:\.[^""]+)?)""[^>]*>(?<body>[\s\S]*?)</PackageReference>",
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    var versionElementPattern = new Regex(
                        @"<Version>\s*(?<version>[^<]+)\s*</Version>",
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

                    var governedRoots = new[]
                    {
                        RootDirectory / "samples",
                        RootDirectory / "templates"
                    };
                    var projectFiles = governedRoots
                        .Where(root => Directory.Exists(root))
                        .SelectMany(root => Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray();

                    foreach (var projectFile in projectFiles)
                    {
                        var relativePath = Path.GetRelativePath(RootDirectory, projectFile).Replace(Path.DirectorySeparatorChar, '/');
                        var source = File.ReadAllText(projectFile);
                        var sourceReferenceViolationCount = 0;
                        var conditionalReferenceViolationCount = 0;
                        var packageVersionViolationCount = 0;

                        foreach (Match match in sourceProjectReferencePattern.Matches(source))
                        {
                            var path = match.Groups["path"].Value;
                            sourceReferenceViolationCount++;
                            failures.Add(new GovernanceFailure(
                                Category: "sample-template-package",
                                InvariantId: SampleTemplatePackageReferencePolicyInvariantId,
                                SourceArtifact: relativePath,
                                Expected: "no source project/import references to src/Agibuild.Fulora",
                                Actual: $"source reference found: '{path}'"));
                        }

                        foreach (Match _ in conditionalProjectReferencePattern.Matches(source))
                        {
                            conditionalReferenceViolationCount++;
                            failures.Add(new GovernanceFailure(
                                Category: "sample-template-package",
                                InvariantId: SampleTemplatePackageReferencePolicyInvariantId,
                                SourceArtifact: relativePath,
                                Expected: "no conditional ProjectReference",
                                Actual: "conditional ProjectReference found"));
                        }

                        foreach (Match match in packageReferenceWithVersionAttributePattern.Matches(source))
                        {
                            var packageId = match.Groups["id"].Value;
                            var version = match.Groups["version"].Value.Trim();
                            if (string.Equals(version, "*-*", StringComparison.Ordinal))
                                continue;

                            packageVersionViolationCount++;
                            failures.Add(new GovernanceFailure(
                                Category: "sample-template-package",
                                InvariantId: SampleTemplatePackageReferencePolicyInvariantId,
                                SourceArtifact: relativePath,
                                Expected: $"package '{packageId}' version '*-*'",
                                Actual: $"version '{version}'"));
                        }

                        foreach (Match match in packageReferenceElementPattern.Matches(source))
                        {
                            var packageId = match.Groups["id"].Value;
                            var body = match.Groups["body"].Value;
                            var versionMatch = versionElementPattern.Match(body);
                            if (!versionMatch.Success)
                                continue;

                            var version = versionMatch.Groups["version"].Value.Trim();
                            if (string.Equals(version, "*-*", StringComparison.Ordinal))
                                continue;

                            packageVersionViolationCount++;
                            failures.Add(new GovernanceFailure(
                                Category: "sample-template-package",
                                InvariantId: SampleTemplatePackageReferencePolicyInvariantId,
                                SourceArtifact: relativePath,
                                Expected: $"package '{packageId}' version '*-*'",
                                Actual: $"version '{version}'"));
                        }

                        checks.Add(new
                        {
                            file = relativePath,
                            sourceReferenceViolationCount,
                            conditionalReferenceViolationCount,
                            packageVersionViolationCount
                        });
                    }

                    var reportPayload = new
                    {
                        generatedAtUtc = DateTime.UtcNow,
                        invariantId = SampleTemplatePackageReferencePolicyInvariantId,
                        governedRoots = SampleTemplateGovernedRootsReportNames,
                        checks,
                        failureCount = failures.Count,
                        failures
                    };

                    return new GovernanceCheckResult(failures, reportPayload);
                });
        });
}
