using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    private static readonly Regex TargetFrameworkRegex = new(
        @"<TargetFrameworks?>\s*(?<tfms>[^<]+)\s*</TargetFrameworks?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool IsCiEnvironment()
        => IsTruthyEnvironmentVariable("CI")
           || IsTruthyEnvironmentVariable("GITHUB_ACTIONS")
           || IsTruthyEnvironmentVariable("TF_BUILD");

    private static bool IsTruthyEnvironmentVariable(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMacOsGuiSmokeEnvironment()
        => OperatingSystem.IsMacOS() && IsCiEnvironment();

    private async Task<AbsolutePath> BuildPlatformAwareSolutionFilterAsync(string filterName)
    {
        var unavailableTfmSuffixes = await DetectUnavailableTfmSuffixesAsync();
        if (unavailableTfmSuffixes.Count == 0)
        {
            Serilog.Log.Information("All platform workloads available — using full solution");
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
                Serilog.Log.Information("Excluding {Project} (requires unavailable platform workload)", relPath);
            }
            else if (HasMissingLocalNuGetSources(fullPath))
            {
                excluded.Add(relPath);
                Serilog.Log.Information("Excluding {Project} (local NuGet source not yet available)", relPath);
            }
            else
            {
                included.Add(relPath);
            }
        }

        if (excluded.Count == 0)
            return SolutionFile;

        ArtifactsDirectory.CreateDirectory();
        var filterPath = ArtifactsDirectory / $"{filterName}.slnf";
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
            "Generated solution filter '{Filter}' with {Included}/{Total} projects (excluded {Excluded} platform-specific)",
            filterName, included.Count, projectPaths.Count, excluded.Count);

        return filterPath;
    }

    private async Task<IReadOnlyList<string>> DetectUnavailableTfmSuffixesAsync()
    {
        var unavailable = new List<string>();

        var hasIos = OperatingSystem.IsMacOS()
                     && await HasDotNetWorkloadAsync("ios")
                     && await HasAppleIosSdkInstalledAsync();
        if (!hasIos)
            unavailable.Add("-ios");

        var hasMacCatalyst = OperatingSystem.IsMacOS()
                             && await HasDotNetWorkloadAsync("maccatalyst")
                             && await HasAppleIosSdkInstalledAsync();
        if (!hasMacCatalyst)
            unavailable.Add("-maccatalyst");

        if (!await HasDotNetWorkloadAsync("android") || !HasAndroidSdkInstalled())
            unavailable.Add("-android");

        return unavailable;
    }

    private static readonly Regex NuGetLocalSourceValueRegex = new(
        @"<add\s[^>]*value\s*=\s*""(?<path>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns true when the project directory contains a nuget.config that references
    /// a local package source directory that does not yet exist (e.g. artifacts/packages
    /// which is only created after the Pack target runs).
    /// </summary>
    private static bool HasMissingLocalNuGetSources(AbsolutePath projectFilePath)
    {
        var projectDir = Path.GetDirectoryName(projectFilePath)!;
        var nugetConfig = Path.Combine(projectDir, "nuget.config");
        if (!File.Exists(nugetConfig))
            return false;

        var content = File.ReadAllText(nugetConfig);
        foreach (Match match in NuGetLocalSourceValueRegex.Matches(content))
        {
            var sourcePath = match.Groups["path"].Value;
            if (sourcePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;

            var resolvedPath = Path.GetFullPath(Path.Combine(projectDir, sourcePath));
            if (!Directory.Exists(resolvedPath))
                return true;
        }

        return false;
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

    private static AbsolutePath? ResolveFirstExistingPath(params AbsolutePath?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate is null)
                continue;

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private async Task<IReadOnlyList<AbsolutePath>> GetProjectsToBuildAsync()
    {
        var projects = new List<AbsolutePath>
        {
            // Core libs (always built)
            SrcDirectory / "Agibuild.Fulora.Core" / "Agibuild.Fulora.Core.csproj",
            SrcDirectory / "Agibuild.Fulora.Adapters.Abstractions" / "Agibuild.Fulora.Adapters.Abstractions.csproj",
            SrcDirectory / "Agibuild.Fulora.Runtime" / "Agibuild.Fulora.Runtime.csproj",
            SrcDirectory / "Agibuild.Fulora.DependencyInjection" / "Agibuild.Fulora.DependencyInjection.csproj",

            // Platform adapters (always built — stub adapters compile on all platforms)
            SrcDirectory / "Agibuild.Fulora.Adapters.Windows" / "Agibuild.Fulora.Adapters.Windows.csproj",
            SrcDirectory / "Agibuild.Fulora.Adapters.Gtk" / "Agibuild.Fulora.Adapters.Gtk.csproj",
        };

        // macOS adapter (native shim requires macOS host)
        if (OperatingSystem.IsMacOS())
        {
            projects.Add(SrcDirectory / "Agibuild.Fulora.Adapters.MacOS" / "Agibuild.Fulora.Adapters.MacOS.csproj");
        }

        // Android adapter (requires workload + Android SDK)
        if (await HasDotNetWorkloadAsync("android") && HasAndroidSdkInstalled())
        {
            projects.Add(SrcDirectory / "Agibuild.Fulora.Adapters.Android" / "Agibuild.Fulora.Adapters.Android.csproj");
        }
        else
        {
            Serilog.Log.Warning("Android workload or SDK not detected — skipping Android adapter build.");
        }

        // iOS adapter (requires macOS host + workload + Xcode iOS SDK)
        if (OperatingSystem.IsMacOS() && await HasDotNetWorkloadAsync("ios") && await HasAppleIosSdkInstalledAsync())
        {
            projects.Add(SrcDirectory / "Agibuild.Fulora.Adapters.iOS" / "Agibuild.Fulora.Adapters.iOS.csproj");
        }
        else if (OperatingSystem.IsMacOS())
        {
            Serilog.Log.Warning("iOS workload or SDK not detected — skipping iOS adapter build.");
        }

        // Main packable project
        projects.Add(SrcDirectory / "Agibuild.Fulora.Avalonia" / "Agibuild.Fulora.Avalonia.csproj");

        // Test projects
        projects.Add(TestsDirectory / "Agibuild.Fulora.Testing" / "Agibuild.Fulora.Testing.csproj");
        projects.Add(TestsDirectory / "Agibuild.Fulora.UnitTests" / "Agibuild.Fulora.UnitTests.csproj");
        projects.Add(IntegrationTestsProject);

        return projects;
    }

    private static string ResolvePackedAgibuildVersion(string packageId)
    {
        var versionPattern = new Regex(
            $"^{Regex.Escape(packageId)}\\.(?<v>\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z\\.]+)?(?:\\+[0-9A-Za-z\\.]+)?)\\.nupkg$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        var packages = PackageOutputDirectory
            .GlobFiles("*.nupkg")
            .Where(p => !p.Name.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .Select(p => new FileInfo(p))
            .Select(p => new { File = p, Match = versionPattern.Match(p.Name) })
            .Where(x => x.Match.Success)
            .OrderByDescending(x => x.File.LastWriteTimeUtc)
            .ToList();

        Assert.NotEmpty(packages, $"No packed nupkg found for {packageId} in {PackageOutputDirectory}.");

        var chosen = packages.First();
        Serilog.Log.Information("Using packed nupkg: {File}", chosen.File.Name);
        return chosen.Match.Groups["v"].Value;
    }

    private static async Task<bool> HasDotNetWorkloadAsync(string platformKeyword)
    {
        try
        {
            var output = await RunProcessAsync("dotnet", ["workload", "list"], timeout: TimeSpan.FromSeconds(30));
            return output.Split('\n')
                .Any(line =>
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.Length == 0 || trimmed.StartsWith('-') || trimmed.StartsWith("Installed", StringComparison.Ordinal) || trimmed.StartsWith("Workload", StringComparison.Ordinal) || trimmed.StartsWith("Use ", StringComparison.Ordinal))
                        return false;
                    var id = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    return id.Equals(platformKeyword, StringComparison.OrdinalIgnoreCase)
                        || id.Split('-').Any(part => part.Equals(platformKeyword, StringComparison.OrdinalIgnoreCase));
                });
        }
        catch
        {
            return false;
        }
    }

    private bool HasAndroidSdkInstalled()
    {
        if (string.IsNullOrWhiteSpace(AndroidSdkRoot))
            return false;

        var sdkRoot = (AbsolutePath)AndroidSdkRoot;
        var adbPath = sdkRoot / "platform-tools" / (OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        return Directory.Exists(sdkRoot) && File.Exists(adbPath);
    }

    private static async Task<bool> HasAppleIosSdkInstalledAsync()
    {
        try
        {
            await RunProcessCheckedAsync("xcrun", ["--sdk", "iphoneos", "--show-sdk-path"], timeout: TimeSpan.FromSeconds(10));
            await RunProcessCheckedAsync("xcodebuild", ["-version"], timeout: TimeSpan.FromSeconds(10));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
