using System.CommandLine;
using System.Diagnostics;
using System.Text;

namespace Agibuild.Fulora.Cli.Commands;

internal static class AddPluginCommand
{
    private const string PluginPrefix = "Agibuild.Fulora.Plugin.";
    private const string NpmPackagePrefix = "@agibuild/fulora-plugin-";

    public static Command Create()
    {
        var packageArg = new Argument<string>("package-name")
        {
            Description = "NuGet package name (e.g. Agibuild.Fulora.Plugin.Database)",
        };
        var projectOpt = new Option<string?>("--project", "-p")
        {
            Description = "Path to the .csproj file (auto-detected if omitted)",
        };

        var command = new Command("plugin") { Description = "Add a Fulora plugin package and optionally its npm companion" };
        command.Arguments.Add(packageArg);
        command.Options.Add(projectOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var packageName = parseResult.GetValue(packageArg) ?? "";
            var project = parseResult.GetValue(projectOpt);
            return await ExecuteAsync(packageName, project, ct);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(string packageName, string? projectPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            Console.Error.WriteLine("Package name is required.");
            return 1;
        }

        var csproj = ResolveCsproj(projectPath);
        if (csproj is null)
        {
            Console.Error.WriteLine("No .csproj file found. Use --project to specify.");
            return 1;
        }

        var dotnetArgs = $"add \"{csproj}\" package \"{packageName.Trim()}\"";
        var dotnetExit = await NewCommand.RunProcessAsync("dotnet", dotnetArgs, Directory.GetCurrentDirectory(), ct);

        if (dotnetExit != 0)
        {
            Console.Error.WriteLine($"dotnet add package failed with exit code {dotnetExit}.");
            return dotnetExit;
        }

        Console.WriteLine($"Added NuGet package: {packageName}");

        var npmPackage = DeriveNpmPackageName(packageName);
        if (npmPackage is not null)
        {
            var npmArgs = $"install {npmPackage}";
            var npmExit = await NewCommand.RunProcessAsync("npm", npmArgs, Directory.GetCurrentDirectory(), ct);

            if (npmExit == 0)
                Console.WriteLine($"Added npm package: {npmPackage}");
            else
                Console.WriteLine($"npm install {npmPackage} failed (exit code {npmExit}). You may add it manually if needed.");
        }

        return 0;
    }

    internal static string? DeriveNpmPackageName(string nugetPackageName)
    {
        if (string.IsNullOrWhiteSpace(nugetPackageName) ||
            !nugetPackageName.StartsWith(PluginPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var suffix = nugetPackageName[PluginPrefix.Length..].Trim();
        if (suffix.Length == 0)
            return null;

        var builder = new StringBuilder(suffix.Length + 4);
        for (var i = 0; i < suffix.Length; i++)
        {
            var ch = suffix[i];
            if (char.IsUpper(ch) && i > 0)
                builder.Append('-');

            builder.Append(char.ToLowerInvariant(ch));
        }

        var npmSuffix = builder.ToString();
        return $"{NpmPackagePrefix}{npmSuffix}";
    }

    internal static string GetDotnetAddPackageArguments(string csproj, string packageName) =>
        $"add \"{csproj}\" package \"{packageName.Trim()}\"";

    private static string? ResolveCsproj(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (File.Exists(explicitPath) && explicitPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(explicitPath);
            if (Directory.Exists(explicitPath))
            {
                var csproj = Directory.GetFiles(explicitPath, "*.csproj").FirstOrDefault();
                return csproj != null ? Path.GetFullPath(csproj) : null;
            }
            return null;
        }

        var cwd = Directory.GetCurrentDirectory();
        var files = Directory.GetFiles(cwd, "*.csproj");
        return files.Length == 1 ? Path.GetFullPath(files[0]) : files.FirstOrDefault();
    }
}
