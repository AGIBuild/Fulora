using System.CommandLine;
using System.Diagnostics;

namespace Agibuild.Fulora.Cli.Commands;

internal static class NewCommand
{
    internal const string DefaultShellPreset = "app-shell";

    public static Command Create()
    {
        var nameArg = new Argument<string>("name") { Description = "Project name" };
        var frontendOpt = new Option<string>("--frontend", "-f")
        {
            Description = "Frontend framework: react, vue, or vanilla",
            Required = true,
        };
        frontendOpt.AcceptOnlyFromAmong("react", "vue", "vanilla");

        var shellPresetOpt = new Option<string?>("--shell-preset")
        {
            Description = "Desktop shell preset: baseline or app-shell",
        };
        shellPresetOpt.AcceptOnlyFromAmong("baseline", "app-shell");

        var command = new Command("new") { Description = "Create a new Agibuild.Fulora hybrid app project" };
        command.Arguments.Add(nameArg);
        command.Options.Add(frontendOpt);
        command.Options.Add(shellPresetOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var name = parseResult.GetValue(nameArg)!;
            var frontend = parseResult.GetValue(frontendOpt)!;
            Console.WriteLine($"Creating project '{name}' with {frontend} frontend...");

            var dotnetArgs = BuildTemplateArguments(name, frontend, parseResult.GetValue(shellPresetOpt));

            var exitCode = await RunProcessAsync("dotnet", dotnetArgs, ct: ct);
            if (exitCode != 0)
            {
                Console.Error.WriteLine($"dotnet new failed with exit code {exitCode}.");
                Console.Error.WriteLine("Ensure the template is installed: dotnet new install Agibuild.Fulora.Templates");
                return exitCode;
            }

            Console.WriteLine();
            Console.WriteLine($"Project '{name}' created successfully!");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine($"  cd {name}");
            Console.WriteLine("  fulora dev");
            return 0;
        });

        return command;
    }

    internal static string BuildTemplateArguments(string name, string frontend, string? shellPreset)
    {
        var resolvedShellPreset = string.IsNullOrWhiteSpace(shellPreset) ? DefaultShellPreset : shellPreset;
        return $"new agibuild-hybrid -n {name} --framework {frontend} --shellPreset {resolvedShellPreset}";
    }

    internal static async Task<int> RunProcessAsync(
        string fileName, string arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
        };

        using var process = Process.Start(psi);
        if (process is null)
            return -1;

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
