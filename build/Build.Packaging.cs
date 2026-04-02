using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal partial class BuildTask
{
    // ──────────────────────────── Records ────────────────────────────

    private sealed record NugetSmokeRetryTelemetry(
        int Attempt,
        string Classification,
        string Outcome,
        string? Message);

    private sealed record NugetPackagesRootResolution(string Path, string Source);

    // ──────────────────────────── Pack / Validate / Publish Targets ────────────────────────────

    internal Target PackageApp => _ => _
        .Description("Publishes an application for a specific runtime (self-contained). Optional; not part of CI.")
        .DependsOn(Build)
        .Requires(() => PackageProject, () => PackageRuntime, () => PackageOutput)
        .Executes(() =>
        {
            var project = (AbsolutePath)PackageProject!;
            var runtime = PackageRuntime!;
            var output = (AbsolutePath)PackageOutput!;
            output.CreateDirectory();

            DotNetPublish(s =>
            {
                var settings = s
                    .SetProject(project)
                    .SetConfiguration(Configuration)
                    .SetRuntime(runtime)
                    .SetOutput(output)
                    .EnableNoRestore()
                    .EnableNoBuild()
                    .SetProperty("PublishSelfContained", "true");

                if (!string.IsNullOrEmpty(PackageAppVersion))
                    settings = settings.SetProperty("Version", PackageAppVersion);

                return settings;
            });

            Serilog.Log.Information("PackageApp output: {Output}", output);
        });

    internal Target Pack => _ => _
        .Description("Creates all NuGet packages: main library and sub-packages (Core, Bridge.Generator, etc.).")
        .DependsOn(Build, Coverage, AutomationLaneReport)
        .Produces(PackageOutputDirectory / "*.nupkg")
        .Executes(() =>
        {
            PackageOutputDirectory.CreateOrCleanDirectory();

            // ── Main package (fat bundle: WebView + adapters) ──
            DotNetPack(s =>
            {
                var settings = s
                    .SetProject(PackProject)
                    .SetConfiguration(Configuration)
                    .EnableNoRestore()
                    .EnableNoBuild()
                    .SetOutputDirectory(PackageOutputDirectory)
                    .SetProperty("SkipPackInputBuilds", "true");

                if (!string.IsNullOrEmpty(VersionSuffix))
                    settings = settings.SetVersionSuffix(VersionSuffix);

                return settings;
            });

            // ── Sub-packages ──
            var subPackageProjects = new[]
            {
                CoreProject,
                AdaptersAbstractionsProject,
                RuntimeProject,
                BridgeGeneratorProject,
                TestingProject,
                CliProject,
                PluginLocalStorageProject,
                PluginHttpClientProject,
                PluginDatabaseProject,
                PluginAuthTokenProject,
                PluginFileSystemProject,
                PluginNotificationsProject,
                OpenTelemetryProject,
            };

            foreach (var project in subPackageProjects)
            {
                DotNetPack(s =>
                {
                    var settings = s
                        .SetProject(project)
                        .SetConfiguration(Configuration)
                        .SetOutputDirectory(PackageOutputDirectory);

                    if (!string.IsNullOrEmpty(VersionSuffix))
                        settings = settings.SetVersionSuffix(VersionSuffix);

                    return settings;
                });
            }
        });

    internal Target ValidatePackage => _ => _
        .Description("Validates the NuGet package contains all expected assemblies and files.")
        .DependsOn(Pack)
        .Executes(() =>
        {
            var nupkgFiles = PackageOutputDirectory.GlobFiles("*.nupkg")
                .Where(f => !f.Name.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.NotEmpty(nupkgFiles, "No .nupkg files found in output directory.");

            const string primaryHostPackageId = "Agibuild.Fulora.Avalonia";
            var mainPackagePrefix = $"{primaryHostPackageId}.";
            var nupkgPath = nupkgFiles.FirstOrDefault(f =>
                    f.Name.StartsWith(mainPackagePrefix, StringComparison.OrdinalIgnoreCase)
                    && f.Name.Length > mainPackagePrefix.Length
                    && char.IsDigit(f.Name[mainPackagePrefix.Length]))
                ?? throw new InvalidOperationException($"Main package {primaryHostPackageId}.*.nupkg not found in output directory.");
            Serilog.Log.Information("Validating package: {Package}", nupkgPath.Name);

            using var archive = ZipFile.OpenRead(nupkgPath);
            var entries = archive.Entries
                .Select(e => e.FullName.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // ── Required assemblies (always present) ──────────────────────────────
            var requiredFiles = new Dictionary<string, string>
            {
                ["lib/net10.0/Agibuild.Fulora.dll"] = "Main assembly",
                ["lib/net10.0/Agibuild.Fulora.Core.dll"] = "Core contracts",
                ["lib/net10.0/Agibuild.Fulora.Adapters.Abstractions.dll"] = "Adapter abstractions",
                ["lib/net10.0/Agibuild.Fulora.Runtime.dll"] = "Runtime host",
                ["lib/net10.0/Agibuild.Fulora.DependencyInjection.dll"] = "DI extensions",
                ["lib/net10.0/Agibuild.Fulora.Adapters.Windows.dll"] = "Windows adapter",
                ["lib/net10.0/Agibuild.Fulora.Adapters.Gtk.dll"] = "Linux GTK adapter",
                ["buildTransitive/Agibuild.Fulora.Avalonia.targets"] = "MSBuild targets",
                ["README.md"] = "Package readme",
            };

            // ── Conditionally expected assemblies ─────────────────────────────────
            var androidAdapterPath = SrcDirectory
                / "Agibuild.Fulora.Adapters.Android" / "bin" / Configuration
                / "net10.0-android" / "Agibuild.Fulora.Adapters.Android.dll";

            var iosAdapterPath = SrcDirectory
                / "Agibuild.Fulora.Adapters.iOS" / "bin" / Configuration
                / "net10.0-ios" / "Agibuild.Fulora.Adapters.iOS.dll";

            var conditionalFiles = new Dictionary<string, (string Description, bool ShouldExist)>
            {
                ["lib/net10.0/Agibuild.Fulora.Adapters.MacOS.dll"] =
                    ("macOS adapter", OperatingSystem.IsMacOS()),
                ["runtimes/osx/native/libAgibuildWebViewWk.dylib"] =
                    ("macOS native shim", OperatingSystem.IsMacOS()),
                ["runtimes/android/lib/net10.0-android36.0/Agibuild.Fulora.Adapters.Android.dll"] =
                    ("Android adapter", File.Exists(androidAdapterPath)),
                ["runtimes/ios/lib/net10.0-ios18.0/Agibuild.Fulora.Adapters.iOS.dll"] =
                    ("iOS adapter", File.Exists(iosAdapterPath)),
            };

            var errors = new List<string>();

            foreach (var (path, description) in requiredFiles)
            {
                if (entries.Contains(path))
                {
                    Serilog.Log.Information("  OK: {Path} ({Description})", path, description);
                }
                else
                {
                    errors.Add($"MISSING (required): {path} — {description}");
                    Serilog.Log.Error("  MISSING: {Path} ({Description})", path, description);
                }
            }

            foreach (var (path, (description, shouldExist)) in conditionalFiles)
            {
                if (entries.Contains(path))
                {
                    Serilog.Log.Information("  OK: {Path} ({Description})", path, description);
                }
                else if (shouldExist)
                {
                    errors.Add($"MISSING (expected on this platform): {path} — {description}");
                    Serilog.Log.Error("  MISSING: {Path} ({Description})", path, description);
                }
                else
                {
                    Serilog.Log.Information("  SKIP: {Path} ({Description} — not built on this platform)", path, description);
                }
            }

            var knownDlls = requiredFiles.Keys
                .Concat(conditionalFiles.Keys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var unexpectedDlls = entries
                .Where(e => e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                         && e.Contains("Agibuild.", StringComparison.OrdinalIgnoreCase)
                         && !knownDlls.Contains(e))
                .ToList();

            foreach (var dll in unexpectedDlls)
            {
                errors.Add($"UNEXPECTED: {dll} — not in the expected manifest");
                Serilog.Log.Warning("  UNEXPECTED: {Path}", dll);
            }

            var nuspecEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspecEntry is not null)
            {
                using var stream = nuspecEntry.Open();
                var nuspecDoc = XDocument.Load(stream);
                var ns = nuspecDoc.Root?.GetDefaultNamespace() ?? XNamespace.None;
                var metadata = nuspecDoc.Root?.Element(ns + "metadata");

                var id = metadata?.Element(ns + "id")?.Value;
                var version = metadata?.Element(ns + "version")?.Value;
                var description = metadata?.Element(ns + "description")?.Value;

                if (string.IsNullOrEmpty(id))
                    errors.Add("NUSPEC: Missing <id>");
                if (string.IsNullOrEmpty(version) || version == "0.0.0-dev" || version == "1.0.0")
                    errors.Add($"NUSPEC: Invalid <version>: '{version}' — version injection may have failed");
                if (string.IsNullOrEmpty(description))
                    errors.Add("NUSPEC: Missing <description>");

                var licenseExpr = metadata?.Element(ns + "license")?.Value;
                var projectUrl = metadata?.Element(ns + "projectUrl")?.Value;

                var isStableVersion = version is not null
                    && !version.Contains('-', StringComparison.Ordinal);

                if (isStableVersion)
                {
                    if (string.IsNullOrEmpty(licenseExpr))
                        errors.Add("NUSPEC: Stable package missing <license> expression");
                    if (string.IsNullOrEmpty(projectUrl))
                        errors.Add("NUSPEC: Stable package missing <projectUrl>");
                    if (description is not null
                        && (description.Contains("preview", StringComparison.OrdinalIgnoreCase)
                            || description.Contains("pre-release", StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"NUSPEC: Stable package description contains preview language: '{description}'");
                }

                Serilog.Log.Information("  Nuspec: id={Id}, version={Version}, license={License}, projectUrl={Url}",
                    id, version, licenseExpr ?? "(none)", projectUrl ?? "(none)");
            }
            else
            {
                errors.Add("NUSPEC: No .nuspec file found in package");
            }

            Serilog.Log.Information("Package validation: {Total} entries, {Errors} error(s)",
                entries.Count, errors.Count);

            if (errors.Count > 0)
            {
                Assert.Fail(
                    "Package validation failed:\n" +
                    string.Join("\n", errors.Select(e => $"  - {e}")));
            }

            Serilog.Log.Information("Package validation PASSED.");
        });

    internal Target Publish => _ => _
        .Description("Pushes all NuGet packages (main library, sub-packages, CLI tool, plugins, and templates) to the configured source.")
        .DependsOn(Pack, PackTemplate)
        .Requires(() => NuGetApiKey)
        .Executes(() =>
        {
            var packages = PackageOutputDirectory.GlobFiles("*.nupkg")
                .Where(p => !p.Name.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase));

            foreach (var package in packages)
            {
                DotNetNuGetPush(s => s
                    .SetTargetPath(package)
                    .SetSource(NuGetSource)
                    .SetApiKey(NuGetApiKey)
                    .EnableSkipDuplicate());
            }
        });

    // ──────────────────────────── npm Publish ────────────────────────────

    internal Target NpmPublish => _ => _
        .Description("Publishes @agibuild/bridge to the npm registry with token-based authentication.")
        .Requires(() => NpmToken)
        .Executes(async () =>
        {
            var bridgeDir = RootDirectory / "packages" / "bridge";

            await RunNpmCaptureAllAsync(["install"], bridgeDir, TimeSpan.FromMinutes(2));
            await RunNpmCaptureAllAsync(["run", "build"], bridgeDir, TimeSpan.FromMinutes(1));
            await RunNpmCaptureAllAsync(["publish", "--access", "public", $"--//registry.npmjs.org/:_authToken={NpmToken}"], bridgeDir, TimeSpan.FromMinutes(1));

            Serilog.Log.Information("@agibuild/bridge published to npm registry.");
        });

    // ──────────────────────────── NuGet Package Smoke Test ────────────────────────────

    internal Target NugetPackageTest => _ => _
        .Description("Packs, restores, builds, and runs the NuGet package integration smoke test end-to-end.")
        .DependsOn(ValidatePackage)
        .OnlyWhenDynamic(
            () => IsMacOsGuiSmokeEnvironment(),
            "Avalonia GUI smoke test runs only on macOS CI agents with a display server")
        .Executes(async () =>
        {
            var packedVersion = ResolvePackedAgibuildVersion("Agibuild.Fulora.Avalonia");
            Serilog.Log.Information("NuGet package smoke pinned to packed version: {Version}", packedVersion);

            var resolvedRoot = await ResolveNugetPackagesRootAsync();
            var nugetPackagesRoot = resolvedRoot.Path;
            Serilog.Log.Information(
                "NuGet global packages root: {Path} (resolved via {Source})",
                nugetPackagesRoot,
                resolvedRoot.Source);

            var agibuildPackageDirs = Directory.Exists(nugetPackagesRoot)
                ? Directory.GetDirectories(nugetPackagesRoot, "agibuild.fulora*")
                : [];
            foreach (var dir in agibuildPackageDirs)
            {
                Serilog.Log.Information("Clearing NuGet cache: {Path}", dir);
                Directory.Delete(dir, recursive: true);
            }
            var afterCleanup = Directory.Exists(nugetPackagesRoot)
                ? Directory.GetDirectories(nugetPackagesRoot, "agibuild.fulora*")
                : [];
            Assert.True(afterCleanup.Length == 0, "Expected clean NuGet cache for Agibuild packages before restore.");

            var testProjectDir = TestsDirectory / "Agibuild.Fulora.Integration.NugetPackageTests";
            var testBinDir = testProjectDir / "bin";
            var testObjDir = testProjectDir / "obj";
            if (Directory.Exists(testBinDir)) testBinDir.DeleteDirectory();
            if (Directory.Exists(testObjDir)) testObjDir.DeleteDirectory();

            Serilog.Log.Information("Restoring NuGet package test project...");
            DotNetRestore(s => s
                .SetProjectFile(NugetPackageTestProject)
                .SetProperty("AgibuildPackageVersion", packedVersion));
            var restoredDirs = Directory.Exists(nugetPackagesRoot)
                ? Directory.GetDirectories(nugetPackagesRoot, "agibuild.fulora*")
                : [];
            Assert.NotEmpty(restoredDirs);
            Serilog.Log.Information("NuGet restore populated {Count} Agibuild package cache path(s).", restoredDirs.Length);

            DotNetBuild(s => s
                .SetProjectFile(NugetPackageTestProject)
                .SetConfiguration(Configuration)
                .SetProperty("AgibuildPackageVersion", packedVersion)
                .EnableNoRestore());

            Serilog.Log.Information("Running NuGet package smoke test...");
            var resultFile = testProjectDir / "bin" / Configuration / "net10.0" / "smoke-test-result.txt";
            if (File.Exists(resultFile)) File.Delete(resultFile);

            var retryTelemetry = new List<NugetSmokeRetryTelemetry>();
            try
            {
                await RunNugetSmokeWithRetryAsync(
                    project: NugetPackageTestProject,
                    retryTelemetry: retryTelemetry,
                    maxAttempts: 3);
            }
            finally
            {
                TestResultsDirectory.CreateDirectory();
                var telemetryPayload = new
                {
                    generatedAtUtc = DateTime.UtcNow,
                    nugetPackagesRoot,
                    resolutionSource = resolvedRoot.Source,
                    attempts = retryTelemetry
                };
                WriteJsonReport(NugetSmokeTelemetryFile, telemetryPayload);
                Serilog.Log.Information("NuGet smoke retry telemetry written to {Path}", NugetSmokeTelemetryFile);
            }

            if (!File.Exists(resultFile))
            {
                Assert.Fail($"Smoke test result file not found at {resultFile}.");
            }

            var result = File.ReadAllText(resultFile).Trim();
            Serilog.Log.Information("Smoke test result: {Result}", result);

            if (!result.StartsWith("PASSED", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail($"NuGet package smoke test FAILED: {result}");
            }

            Serilog.Log.Information("NuGet package integration test PASSED.");
        });

    // ──────────────────────────── NuGet Helpers ────────────────────────────

    private static async Task<NugetPackagesRootResolution> ResolveNugetPackagesRootAsync()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return new NugetPackagesRootResolution(
                NormalizePath(fromEnv),
                "NUGET_PACKAGES");
        }

        try
        {
            var output = await RunProcessAsync("dotnet", ["nuget", "locals", "global-packages", "--list"], timeout: TimeSpan.FromSeconds(15));
            const string marker = "global-packages:";
            var pathFromCli = output
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                .Select(line => line[(line.IndexOf(':') + 1)..].Trim())
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(pathFromCli))
            {
                return new NugetPackagesRootResolution(
                    NormalizePath(pathFromCli),
                    "dotnet-nuget-locals");
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Failed to resolve NuGet root via dotnet CLI: {Message}", ex.Message);
        }

        return new NugetPackagesRootResolution(
            NormalizePath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages")),
            "user-profile-default");
    }

    private static string NormalizePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.GetFullPath(expanded);
    }

    private static string ClassifyNugetSmokeFailure(string message)
    {
        var transientMarkers = new[]
        {
            "XARLP7000",
            "Xamarin.Tools.Zip.ZipException",
            "being used by another process",
            "The process cannot access the file",
            "An existing connection was forcibly closed",
            "Unable to load the service index",
            "The SSL connection could not be established",
            "timed out"
        };

        return transientMarkers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? "transient"
            : "deterministic";
    }

    private async Task RunNugetSmokeWithRetryAsync(AbsolutePath project, IList<NugetSmokeRetryTelemetry> retryTelemetry, int maxAttempts)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var output = await RunProcessCheckedAsync(
                    "dotnet",
                    ["run", "--project", project, "--configuration", Configuration, "--no-restore", "--no-build", "--", "--smoke-test"],
                    workingDirectory: RootDirectory,
                    timeout: TimeSpan.FromMinutes(1));

                if (output.Contains("Failed to unregister class Chrome_WidgetWin_0", StringComparison.Ordinal) ||
                    output.Contains("ui\\gfx\\win\\window_impl.cc:124", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "NuGet smoke produced a Chromium teardown error: " +
                        "Failed to unregister class Chrome_WidgetWin_0 (window_impl.cc:124).");
                }

                retryTelemetry.Add(new NugetSmokeRetryTelemetry(
                    Attempt: attempt,
                    Classification: attempt == 1 ? "none" : "transient",
                    Outcome: "success",
                    Message: null));
                return;
            }
            catch (Exception ex)
            {
                var message = ex.ToString();
                var classification = ClassifyNugetSmokeFailure(message);
                var isFinalAttempt = attempt >= maxAttempts || string.Equals(classification, "deterministic", StringComparison.Ordinal);
                var outcome = isFinalAttempt ? "failed" : "retrying";

                retryTelemetry.Add(new NugetSmokeRetryTelemetry(
                    Attempt: attempt,
                    Classification: classification,
                    Outcome: outcome,
                    Message: ex.Message));

                if (isFinalAttempt)
                {
                    throw;
                }

                var delayMs = 1000 * attempt;
                Serilog.Log.Warning(
                    "NuGet smoke attempt {Attempt}/{MaxAttempts} failed ({Classification}). Retrying after {DelayMs}ms...",
                    attempt,
                    maxAttempts,
                    classification,
                    delayMs);
                await Task.Delay(delayMs);
            }
        }
    }
}
