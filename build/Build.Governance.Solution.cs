using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    internal Target SolutionConsistencyGovernance => _ => _
        .Description("Validates that all on-disk projects are included in the solution file.")
        .Executes(() =>
        {
            RunGovernanceCheck(
                "Solution consistency governance",
                SolutionConsistencyGovernanceReportFile,
                () =>
                {
                    var failures = new List<string>();
                    var slnPath = RootDirectory / "Agibuild.Fulora.sln";

                    var slnContent = File.ReadAllText(slnPath);

                    var projectDirs = new[] { "src", "plugins", "tests" };
                    var onDiskProjects = projectDirs
                        .Select(d => RootDirectory / d)
                        .Where(d => Directory.Exists(d))
                        .SelectMany(d => Directory.GetFiles(d, "*.csproj", SearchOption.AllDirectories))
                        .Select(p => Path.GetRelativePath(RootDirectory, p).Replace('\\', '/'))
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var missingFromSln = new List<string>();
                    foreach (var project in onDiskProjects)
                    {
                        var projectName = Path.GetFileNameWithoutExtension(project);
                        if (!slnContent.Contains(projectName, StringComparison.OrdinalIgnoreCase))
                        {
                            missingFromSln.Add(project);
                            failures.Add($"Project '{project}' exists on disk but is not in Agibuild.Fulora.sln");
                        }
                    }

                    var reportPayload = new
                    {
                        generatedAtUtc = DateTime.UtcNow,
                        totalOnDisk = onDiskProjects.Count,
                        missingFromSolution = missingFromSln,
                        failureCount = failures.Count,
                        failures
                    };

                    return new GovernanceCheckResult(failures, reportPayload);
                });
        });
}
