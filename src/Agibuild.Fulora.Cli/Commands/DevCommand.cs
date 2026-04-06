using System.CommandLine;
using System.Diagnostics;

namespace Agibuild.Fulora.Cli.Commands;

internal static class DevCommand
{
    internal delegate Task<int> ProcessRunner(string fileName, string arguments, string? workingDirectory, CancellationToken ct);

    public static Command Create()
    {
        var webDirOpt = new Option<string?>("--web")
        {
            Description = "Path to web project directory (auto-detected if omitted)"
        };
        var desktopDirOpt = new Option<string?>("--desktop")
        {
            Description = "Path to Desktop .csproj (auto-detected if omitted)"
        };
        var npmScriptOpt = new Option<string>("--npm-script")
        {
            Description = "npm script to start the dev server",
            DefaultValueFactory = _ => "dev"
        };
        var preflightOnlyOpt = new Option<bool>("--preflight-only")
        {
            Description = "Run bridge/dev preflight checks and exit without starting the dev processes"
        };

        var command = new Command("dev") { Description = "Start Vite dev server and Avalonia desktop app together" };
        command.Options.Add(webDirOpt);
        command.Options.Add(desktopDirOpt);
        command.Options.Add(npmScriptOpt);
        command.Options.Add(preflightOnlyOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var webDir = parseResult.GetValue(webDirOpt);
            var desktopDir = parseResult.GetValue(desktopDirOpt);
            var npmScript = parseResult.GetValue(npmScriptOpt) ?? "dev";
            var preflightOnly = parseResult.GetValue(preflightOnlyOpt);

            var web = webDir ?? DetectWebProject();
            var desktop = desktopDir ?? DetectDesktopProject();

            if (web is null)
            {
                Console.Error.WriteLine("Could not find web project (package.json). Use --web to specify.");
                return 1;
            }

            if (desktop is null)
            {
                Console.Error.WriteLine("Could not find Desktop .csproj. Use --desktop to specify.");
                return 1;
            }

            var preflightExitCode = await PrepareBridgeArtifactsAsync(
                explicitBridgeProject: null,
                output: Console.Out,
                error: Console.Error,
                runProcessAsync: NewCommand.RunProcessAsync,
                ct);
            if (preflightExitCode != 0)
                return preflightExitCode;

            if (preflightOnly)
            {
                Console.WriteLine("Preflight complete.");
                return 0;
            }

            Console.WriteLine($"Starting dev server in {Path.GetFileName(web)}...");
            Console.WriteLine($"Starting Avalonia app from {Path.GetFileName(desktop)}...");
            Console.WriteLine("Press Ctrl+C to stop both.");
            Console.WriteLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var npmCmd = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
            var viteTask = RunProcessUntilCancelledAsync(npmCmd, $"run {npmScript}", web, "[vite]", cts.Token);
            var dotnetTask = RunProcessUntilCancelledAsync("dotnet", $"run --project \"{desktop}\"", null, "[dotnet]", cts.Token);

            try
            {
                await Task.WhenAll(viteTask, dotnetTask);
            }
            catch (OperationCanceledException)
            {
                // Expected on Ctrl+C
            }

            Console.WriteLine();
            Console.WriteLine("Development servers stopped.");
            return 0;
        });

        return command;
    }

    internal static async Task<int> PrepareBridgeArtifactsAsync(
        string? explicitBridgeProject,
        TextWriter output,
        TextWriter error,
        ProcessRunner runProcessAsync,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(runProcessAsync);

        var bridgeProject = explicitBridgeProject ?? GenerateCommand.DetectBridgeProject();
        if (bridgeProject is null)
        {
            output.WriteLine("No Bridge .csproj detected; skipping bridge artifact preflight.");
            return 0;
        }

        output.WriteLine($"Refreshing bridge artifacts from {Path.GetFileName(bridgeProject)}...");
        var bridgeProjectDirectory = Path.GetDirectoryName(bridgeProject);
        var exitCode = await runProcessAsync("dotnet", $"build \"{bridgeProject}\" -v q -m:1 -nodeReuse:false", bridgeProjectDirectory, ct);
        if (exitCode == 0)
        {
            output.WriteLine("Bridge artifacts ready.");
            EmitArtifactConsistencyWarnings(bridgeProject, output);
            return 0;
        }

        error.WriteLine("Bridge artifact generation failed during dev preflight.");
        error.WriteLine($"Fix the bridge build and rerun `fulora dev`, or inspect generation manually with `fulora generate types --project \"{bridgeProject}\"`.");
        return exitCode;
    }

    private static void EmitArtifactConsistencyWarnings(string bridgeProject, TextWriter output)
    {
        var warnings = GenerateCommand.CollectArtifactConsistencyWarnings(bridgeProject);
        if (warnings.Count == 0)
            return;

        foreach (var line in BridgeArtifactConsistency.FormatWarnings(warnings))
            output.WriteLine(line);
    }

    private static async Task RunProcessUntilCancelledAsync(
        string fileName, string arguments, string? workingDirectory, string prefix, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
        };

        using var process = Process.Start(psi);
        if (process is null) return;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(ct);
                if (line is null) break;
                Console.WriteLine($"{prefix} {line}");
            }
        }, ct);

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(ct);
                if (line is null) break;
                Console.Error.WriteLine($"{prefix} {line}");
            }
        }, ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* Best effort */ }
        }
    }

    private static string? DetectWebProject()
    {
        var cwd = Directory.GetCurrentDirectory();
        var packageJsons = Directory.GetFiles(cwd, "package.json", SearchOption.AllDirectories)
            .Where(p => !p.Contains("node_modules"))
            .ToArray();

        foreach (var pj in packageJsons)
        {
            var dir = Path.GetDirectoryName(pj)!;
            var name = Path.GetFileName(dir);
            if (name.Contains("Web", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Vite", StringComparison.OrdinalIgnoreCase))
                return dir;
        }

        return packageJsons.Length == 1 ? Path.GetDirectoryName(packageJsons[0]) : null;
    }

    private static string? DetectDesktopProject()
    {
        var cwd = Directory.GetCurrentDirectory();
        var csprojs = Directory.GetFiles(cwd, "*.Desktop.csproj", SearchOption.AllDirectories);
        return csprojs.Length == 1 ? csprojs[0] : csprojs.FirstOrDefault();
    }
}
