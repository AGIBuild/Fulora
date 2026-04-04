using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;

namespace Agibuild.Fulora.Cli.Commands;

internal static class PackageCommand
{
    public static Command Create()
    {
        var profileOpt = new Option<string?>("--profile")
        {
            Description = "Packaging profile (recommended: desktop-public, desktop-internal, mac-notarized)"
        };
        var projectOpt = new Option<string?>("--project", "-p")
        {
            Description = "Path to the .csproj (required)"
        };
        var runtimeOpt = new Option<string>("--runtime", "-r")
        {
            Description = "Target runtime identifier (win-x64, osx-arm64, linux-x64, etc.)",
            DefaultValueFactory = _ => "win-x64"
        };
        var versionOpt = new Option<string?>("--version", "-v")
        {
            Description = "Version for the package (semver). Defaults to project version."
        };
        var outputOpt = new Option<string?>("--output", "-o")
        {
            Description = "Output directory for packages. Default: ./Releases"
        };
        var iconOpt = new Option<string?>("--icon", "-i")
        {
            Description = "Path to icon file for the package"
        };
        var signParamsOpt = new Option<string?>("--sign-params", "-n")
        {
            Description = "Code signing parameters (platform-specific, passed to vpk)"
        };
        var notarizeOpt = new Option<bool>("--notarize")
        {
            Description = "Enable macOS notarization (uses --notaryProfile default when vpk available)"
        };
        var channelOpt = new Option<string>("--channel", "-c")
        {
            Description = "Release channel",
            DefaultValueFactory = _ => "stable"
        };

        var command = new Command("package")
        {
            Description = "Package application for distribution. Start with --profile desktop-public, desktop-internal, or mac-notarized."
        };
        command.Options.Add(profileOpt);
        command.Options.Add(projectOpt);
        command.Options.Add(runtimeOpt);
        command.Options.Add(versionOpt);
        command.Options.Add(outputOpt);
        command.Options.Add(iconOpt);
        command.Options.Add(signParamsOpt);
        command.Options.Add(notarizeOpt);
        command.Options.Add(channelOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var profileName = parseResult.GetValue(profileOpt);
            var project = parseResult.GetValue(projectOpt);
            var version = parseResult.GetValue(versionOpt);
            var output = parseResult.GetValue(outputOpt);
            var icon = parseResult.GetValue(iconOpt);
            var signParams = parseResult.GetValue(signParamsOpt);
            if (!TryResolveProfile(profileName, out var profile))
            {
                return 1;
            }

            var runtime = GetValue(runtimeOpt, parseResult, profile.Runtime);
            var notarize = GetValue(notarizeOpt, parseResult, profile.Notarize);
            var channel = GetValue(channelOpt, parseResult, profile.Channel)!;

            if (string.IsNullOrWhiteSpace(project))
            {
                Console.Error.WriteLine("--project is required. Specify the path to your .csproj.");
                return 1;
            }

            project = project.Trim();

            var projectPath = Path.GetFullPath(project);
            if (!File.Exists(projectPath))
            {
                Console.Error.WriteLine($"Project file not found: {projectPath}");
                return 1;
            }

            var projectDir = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var packId = projectName;
            var packVersion = version ?? GetProjectVersion(projectPath) ?? "1.0.0";
            var outputDir = Path.GetFullPath(output ?? Path.Combine(projectDir, "Releases"));
            var publishDir = Path.Combine(Path.GetTempPath(), "FuloraPackage", Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(publishDir);

                var publishArgs = $"publish \"{projectPath}\" -c Release -r {runtime} --self-contained -o \"{publishDir}\"";
                Console.WriteLine($"Running: dotnet {publishArgs}");
                var exitCode = await NewCommand.RunProcessAsync("dotnet", publishArgs, projectDir, ct);
                if (exitCode != 0)
                {
                    Console.Error.WriteLine("dotnet publish failed.");
                    return exitCode;
                }

                var mainExe = GetMainExeName(projectName, runtime ?? "win-x64");
                var mainExePath = Path.Combine(publishDir, mainExe);
                if (!File.Exists(mainExePath))
                {
                    var fallback = Directory.GetFiles(publishDir, "*.exe").FirstOrDefault()
                        ?? Directory.GetFiles(publishDir).FirstOrDefault(f => !Path.GetExtension(f).Equals(".dll", StringComparison.OrdinalIgnoreCase));
                    mainExe = fallback is not null ? Path.GetFileName(fallback) : mainExe;
                }

                var vpkPath = FindVpk();
                if (vpkPath is not null)
                {
                    var vpkArgs = $"pack -u {packId} -v {packVersion} -p \"{publishDir}\" -e \"{mainExe}\" -o \"{outputDir}\" -c {channel}";
                    if (!string.IsNullOrWhiteSpace(icon))
                        vpkArgs += $" -i \"{Path.GetFullPath(icon)}\"";
                    if (!string.IsNullOrWhiteSpace(signParams))
                        vpkArgs += $" -n \"{signParams}\"";
                    if (notarize && OperatingSystem.IsMacOS())
                        vpkArgs += " --notaryProfile default";

                    Console.WriteLine($"Running: vpk {vpkArgs}");
                    exitCode = await NewCommand.RunProcessAsync(vpkPath, vpkArgs, null, ct);
                    if (exitCode != 0)
                    {
                        Console.Error.WriteLine("vpk pack failed.");
                        return exitCode;
                    }
                }
                else
                {
                    Directory.CreateDirectory(outputDir);
                    var destDir = Path.Combine(outputDir, $"{packId}-{packVersion}");
                    if (Directory.Exists(destDir))
                        Directory.Delete(destDir, recursive: true);
                    CopyDirectory(publishDir, destDir);
                    Console.WriteLine($"Packaged to {destDir} (vpk not found; copied publish output)");
                }

                Console.WriteLine();
                Console.WriteLine($"Packages created in: {outputDir}");
                return 0;
            }
            finally
            {
                if (Directory.Exists(publishDir))
                {
                    try { Directory.Delete(publishDir, recursive: true); }
                    catch { /* Ignore */ }
                }
            }
        });

        return command;
    }

    private static string? GetProjectVersion(string projectPath)
    {
        try
        {
            var xml = File.ReadAllText(projectPath);
            var match = System.Text.RegularExpressions.Regex.Match(xml, @"<Version>(.*?)</Version>");
            if (match.Success)
                return match.Groups[1].Value.Trim();
            match = System.Text.RegularExpressions.Regex.Match(xml, @"<VersionPrefix>(.*?)</VersionPrefix>");
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }
        catch { /* Ignore */ }
        return null;
    }

    private static string GetMainExeName(string projectName, string runtime)
    {
        if (runtime.StartsWith("win", StringComparison.OrdinalIgnoreCase))
            return $"{projectName}.exe";
        return projectName;
    }

    private static string? FindVpk()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var vpk = Path.Combine(dir.Trim(), OperatingSystem.IsWindows() ? "vpk.exe" : "vpk");
            if (File.Exists(vpk))
                return vpk;
        }
        return null;
    }

    private static bool TryResolveProfile(string? profileName, out PackageProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            profile = new PackageProfile(string.Empty, "stable", null, false);
            return true;
        }

        if (PackageProfileDefaults.TryResolve(profileName, out profile))
        {
            return true;
        }

        Console.Error.WriteLine($"Unknown package profile '{profileName}'. Supported profiles: desktop-internal, desktop-public, mac-notarized.");
        return false;
    }

    private static T GetValue<T>(Option<T> option, ParseResult parseResult, T? profileDefault)
    {
        var optionResult = parseResult.GetResult(option);
        if (optionResult is OptionResult { Implicit: false })
        {
            return parseResult.GetValue(option)!;
        }

        return profileDefault is not null ? profileDefault : parseResult.GetValue(option)!;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
