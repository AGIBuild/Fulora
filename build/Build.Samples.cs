using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

partial class BuildTask
{
    static async Task EnsureNpmAvailableAsync(string workingDirectory)
    {
        try
        {
            await RunNpmCheckedAsync(["--version"], workingDirectory, TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                $"npm is required but is not available. Install Node.js and make sure npm is on PATH. Working directory: '{workingDirectory}'.",
                ex);
        }
    }

    async Task EnsureSampleWebDepsInstalledAsync(AbsolutePath webDirectory)
    {
        Assert.DirectoryExists(webDirectory, $"Web project not found at {webDirectory}.");
        await EnsureNpmAvailableAsync(webDirectory);

        var nodeModules = webDirectory / "node_modules";
        var installStamp = nodeModules / ".install-stamp";
        var bridgeRuntimeEntry = nodeModules / "@agibuild" / "bridge" / "dist" / "index.js";

        if (File.Exists(installStamp) && File.Exists(bridgeRuntimeEntry))
        {
            Serilog.Log.Information("npm dependencies already installed (stamp file present) in {Dir}.", webDirectory);
            return;
        }

        if (!Directory.Exists(nodeModules))
        {
            Serilog.Log.Information("node_modules not found in {Dir}, running npm install...", webDirectory);
            await RunNpmCheckedAsync(["install"], webDirectory, TimeSpan.FromMinutes(2));
            Serilog.Log.Information("npm install completed.");
            return;
        }

        if (!File.Exists(bridgeRuntimeEntry))
        {
            Serilog.Log.Information("Bridge runtime entry not found at {Path}, refreshing npm install...", bridgeRuntimeEntry);
            await RunNpmCheckedAsync(["install"], webDirectory, TimeSpan.FromMinutes(2));
            Serilog.Log.Information("npm install completed.");
        }
    }

    static async Task<bool> IsHttpReadyAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await http.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    static async Task WaitForPortAsync(int port, int timeoutSeconds = 30)
    {
        var url = $"http://localhost:{port}";
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await IsHttpReadyAsync(url)) return;
            await Task.Delay(500);
        }

        throw new TimeoutException($"{url} did not become available within {timeoutSeconds}s.");
    }

    static ProcessStartInfo CreateNpmStartInfo(string arguments, string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /s /c \"npm {arguments}\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = false,
            };
        }

        return new ProcessStartInfo
        {
            FileName = "npm",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
    }

    async Task StartDesktopAppAsync(AbsolutePath desktopProject, AbsolutePath webDirectory, int devPort)
    {
        Assert.FileExists(desktopProject, $"Desktop project not found at {desktopProject}.");

        Process? viteProcess = null;

        if (string.Equals(Configuration, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            var devUrl = $"http://localhost:{devPort}";
            if (await IsHttpReadyAsync(devUrl))
            {
                Serilog.Log.Information("Vite dev server already running on port {Port}.", devPort);
            }
            else
            {
                await EnsureSampleWebDepsInstalledAsync(webDirectory);

                Serilog.Log.Information("Starting Vite dev server in background...");
                viteProcess = new Process
                {
                    StartInfo = CreateNpmStartInfo("run dev", webDirectory)
                };
                viteProcess.Start();

                await WaitForPortAsync(devPort, timeoutSeconds: 60);
                Serilog.Log.Information("Vite dev server is ready on http://localhost:{Port}", devPort);
            }
        }

        try
        {
            DotNetRun(s => s
                .SetProjectFile(desktopProject)
                .SetConfiguration(Configuration));
        }
        finally
        {
            if (viteProcess is { HasExited: false })
            {
                Serilog.Log.Information("Stopping Vite dev server...");
                try { viteProcess.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
                viteProcess.Dispose();
            }
        }
    }

    // ──────────────────────────── Sample App Targets ────────────────────────────

    Target StartAiChatApp => _ => _
        .Description("Launches the AI Chat sample. Ensures Ollama is running and the required model is available.")
        .Executes(async () =>
        {
            await EnsureOllamaReadyAsync();
            await StartDesktopAppAsync(AiChatDesktopProject, AiChatWebDirectory, devPort: 5175);
        });

    Target StartReactApp => _ => _
        .Description("Launches the React sample. In Debug: auto-starts Vite dev server if needed.")
        .Executes(async () => await StartDesktopAppAsync(ReactDesktopProject, ReactWebDirectory, devPort: 5173));

    Target StartVueApp => _ => _
        .Description("Launches the Vue sample. In Debug: auto-starts Vite dev server if needed.")
        .Executes(async () => await StartDesktopAppAsync(VueDesktopProject, VueWebDirectory, devPort: 5174));

    Target StartTodoApp => _ => _
        .Description("Launches the Showcase Todo sample. In Debug: auto-starts Vite dev server if needed.")
        .Executes(async () => await StartDesktopAppAsync(TodoDesktopProject, TodoWebDirectory, devPort: 5176));

    Target StartMinimalApp => _ => _
        .Description("Launches the Minimal Hybrid sample (static wwwroot, no Vite).")
        .Executes(() =>
        {
            Assert.FileExists(MinimalHybridDesktopProject, $"Desktop project not found at {MinimalHybridDesktopProject}.");
            DotNetRun(s => s
                .SetProjectFile(MinimalHybridDesktopProject)
                .SetConfiguration(Configuration));
        });

    // ──────────────────────────── Ollama Bootstrapping ───────────────────────────

    const string OllamaEndpoint = "http://localhost:11434";
    const string OllamaModel = "qwen2.5:3b";

    static Process? _ollamaServeProcess;

    async Task EnsureOllamaReadyAsync()
    {
        var echoMode = string.Equals(
            Environment.GetEnvironmentVariable("AI__PROVIDER"), "echo", StringComparison.OrdinalIgnoreCase);
        if (echoMode)
        {
            Serilog.Log.Information("AI__PROVIDER=echo — skipping Ollama bootstrap.");
            return;
        }

        if (!await IsToolAvailableAsync("ollama"))
        {
            Serilog.Log.Warning(
                "ollama is not installed. The app will prompt the user to install it. " +
                "Download from https://ollama.com/download");
            return;
        }

        Serilog.Log.Information("ollama is installed.");

        var apiUrl = $"{OllamaEndpoint.TrimEnd('/')}/api/tags";
        if (!await IsHttpReadyAsync(apiUrl))
        {
            Serilog.Log.Information("Ollama API is not reachable at {Url}. Starting 'ollama serve' in background...", OllamaEndpoint);
            _ollamaServeProcess = StartOllamaServe();
            await WaitForOllamaApiAsync(timeoutSeconds: 20);
            Serilog.Log.Information("Ollama API is now reachable.");
        }
        else
        {
            Serilog.Log.Information("Ollama API is already running at {Url}.", OllamaEndpoint);
        }

        if (await IsModelAvailableAsync(OllamaModel))
        {
            Serilog.Log.Information("Model '{Model}' is already available.", OllamaModel);
        }
        else
        {
            Serilog.Log.Information("Model '{Model}' not found. Pulling (this may take a few minutes)...", OllamaModel);
            await PullOllamaModelAsync(OllamaModel);
            Serilog.Log.Information("Model '{Model}' is now available.", OllamaModel);
        }
    }

    static Process StartOllamaServe()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ollama",
            Arguments = "serve",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true,
        };
        var process = new Process { StartInfo = psi };
        process.Start();
        return process;
    }

    async Task WaitForOllamaApiAsync(int timeoutSeconds = 20)
    {
        var apiUrl = $"{OllamaEndpoint.TrimEnd('/')}/api/tags";
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await IsHttpReadyAsync(apiUrl)) return;
            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"Ollama API at {OllamaEndpoint} did not become available within {timeoutSeconds}s. " +
            "Check that 'ollama serve' is working correctly.");
    }

    static async Task<bool> IsModelAvailableAsync(string model)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await http.GetAsync($"{OllamaEndpoint.TrimEnd('/')}/api/tags");
            if (!response.IsSuccessStatusCode) return false;
            var body = await response.Content.ReadAsStringAsync();
            return body.Contains($"\"name\":\"{model}\"", StringComparison.OrdinalIgnoreCase)
                || body.Contains($"\"name\": \"{model}\"", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static async Task PullOllamaModelAsync(string model)
    {
        var output = await RunProcessCheckedAsync("ollama", ["pull", model], timeout: TimeSpan.FromMinutes(10));
        Serilog.Log.Debug("ollama pull output: {Output}", output.Trim());
    }
}
