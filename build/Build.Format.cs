using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.IO;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal partial class BuildTask
{
    private static readonly Regex TargetFrameworkRegex = new(
        @"<TargetFrameworks?>\s*(?<tfms>[^<]+)\s*</TargetFrameworks?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal Target Format => _ => _
        .Description("Verifies that code formatting matches .editorconfig rules. Fails if any files would be changed.")
        .DependsOn(Restore)
        .Executes(async () =>
        {
            var filterPath = await BuildFormatSolutionFilterAsync();
            DotNet($"format {filterPath} --verify-no-changes", workingDirectory: RootDirectory);
        });

    private async Task<AbsolutePath> BuildFormatSolutionFilterAsync()
    {
        var unavailableTfmSuffixes = await DetectUnavailableTfmSuffixesAsync();
        if (unavailableTfmSuffixes.Count == 0)
        {
            Serilog.Log.Information("All platform workloads available — using full solution for format check");
            return SolutionFile;
        }

        var slnContent = await File.ReadAllTextAsync(SolutionFile);
        var projectPaths = ParseProjectPathsFromSolution(slnContent);

        var included = new List<string>();
        var excluded = new List<string>();

        foreach (var relPath in projectPaths)
        {
            var fullPath = RootDirectory / relPath;
            if (!File.Exists(fullPath))
            {
                excluded.Add(relPath);
                continue;
            }

            var csproj = await File.ReadAllTextAsync(fullPath);
            if (RequiresUnavailableWorkload(csproj, unavailableTfmSuffixes))
            {
                excluded.Add(relPath);
                Serilog.Log.Information("Format: excluding {Project} (requires unavailable platform workload)", relPath);
            }
            else
            {
                included.Add(relPath);
            }
        }

        if (excluded.Count == 0)
            return SolutionFile;

        ArtifactsDirectory.CreateDirectory();
        var filterPath = ArtifactsDirectory / "format-check.slnf";
        var solutionRelativePath = Path.GetRelativePath(ArtifactsDirectory, SolutionFile);

        var filter = new
        {
            solution = new
            {
                path = solutionRelativePath,
                projects = included
            }
        };

        await File.WriteAllTextAsync(filterPath, JsonSerializer.Serialize(filter, WriteIndentedJsonOptions));
        Serilog.Log.Information(
            "Format: generated solution filter with {Included}/{Total} projects (excluded {Excluded} platform-specific)",
            included.Count, projectPaths.Count, excluded.Count);

        return filterPath;
    }

    private async Task<IReadOnlyList<string>> DetectUnavailableTfmSuffixesAsync()
    {
        var unavailable = new List<string>();

        if (!OperatingSystem.IsMacOS() || !await HasDotNetWorkloadAsync("ios"))
            unavailable.Add("-ios");

        if (!OperatingSystem.IsMacOS() || !await HasDotNetWorkloadAsync("maccatalyst"))
            unavailable.Add("-maccatalyst");

        if (!await HasDotNetWorkloadAsync("android") || !HasAndroidSdkInstalled())
            unavailable.Add("-android");

        return unavailable;
    }

    private static bool RequiresUnavailableWorkload(string csprojContent, IReadOnlyList<string> unavailableSuffixes)
    {
        foreach (Match match in TargetFrameworkRegex.Matches(csprojContent))
        {
            var tfmValue = match.Groups["tfms"].Value;
            foreach (var suffix in unavailableSuffixes)
            {
                if (tfmValue.Contains(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ParseProjectPathsFromSolution(string slnContent)
    {
        var projectLineRegex = new Regex(
            @"Project\("".+""\)\s*=\s*"".+""\s*,\s*""(?<path>[^""]+\.csproj)""\s*,",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        return projectLineRegex
            .Matches(slnContent)
            .Select(m => m.Groups["path"].Value.Replace('\\', '/'))
            .ToList();
    }
}
