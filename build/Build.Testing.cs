using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal partial class BuildTask
{
    private void RestoreTestProject(AbsolutePath projectFile)
    {
        DotNetRestore(s => s
            .SetProjectFile(projectFile));
    }

    private void BuildTestProject(AbsolutePath projectFile)
    {
        DotNetBuild(s => s
            .SetProjectFile(projectFile)
            .SetConfiguration(Configuration)
            .EnableNoRestore());
    }

    private void RunFastTestProject(AbsolutePath projectFile, string trxFileName)
    {
        DotNetTest(s => s
            .SetProjectFile(projectFile)
            .SetConfiguration(Configuration)
            .SetResultsDirectory(TestResultsDirectory)
            .SetLoggers($"trx;LogFileName={trxFileName}"));
    }

    private void RunUnitTestProject(AbsolutePath projectFile, string trxFileName)
    {
        DotNetTest(s => s
            .SetProjectFile(projectFile)
            .SetConfiguration(Configuration)
            .EnableNoRestore()
            .EnableNoBuild()
            .SetResultsDirectory(TestResultsDirectory)
            .SetLoggers($"trx;LogFileName={trxFileName}"));
    }

    private void RunHotFastTestProject(AbsolutePath projectFile, string trxFileName)
    {
        DotNetTest(s => s
            .SetProjectFile(projectFile)
            .SetConfiguration(Configuration)
            .EnableNoRestore()
            .EnableNoBuild()
            .SetResultsDirectory(TestResultsDirectory)
            .SetLoggers($"trx;LogFileName={trxFileName}"));
    }

    internal Target FastTestBuild => _ => _
        .Description("Restores and builds only the fast CLI test project for quick reruns.")
        .Executes(() =>
        {
            RestoreTestProject(CliUnitTestsProject);
            BuildTestProject(CliUnitTestsProject);
        });

    internal Target FastUnitTests => _ => _
        .Description("Runs the fast CLI-focused unit test lane after a targeted fast build.")
        .DependsOn(FastTestBuild)
        .Executes(() =>
        {
            TestResultsDirectory.CreateDirectory();
            RunHotFastTestProject(CliUnitTestsProject, "cli-fast-unit-tests.trx");
        });

    internal Target UnitTests => _ => _
        .Description("Runs unit tests.")
        .DependsOn(Build)
        .Executes(() =>
        {
            RunUnitTestProject(UnitTestsProject, "unit-tests.trx");
            RunUnitTestProject(CliUnitTestsProject, "cli-unit-tests.trx");
        });

    internal Target MaciosUnitTests => _ => _
        .Description("Runs macOS-host Macios.Interop sanity tests (Apple platforms only).")
        .DependsOn(Build)
        .OnlyWhenDynamic(() => OperatingSystem.IsMacOS())
        .Executes(() =>
        {
            RunFastTestProject(PlatformsUnitTestsProject, "macios-unit-tests.trx");
        });

    internal Target Coverage => _ => _
        .Description("Runs unit tests with code coverage and enforces minimum threshold.")
        .DependsOn(Build)
        .Executes(async () =>
        {
            CoverageDirectory.CreateOrCleanDirectory();
            CoverageReportDirectory.CreateOrCleanDirectory();
            TestResultsDirectory.CreateDirectory();

            await RunCoverageUnitTestsAsync(CoverageDirectory, "unit-tests.trx");

            // Mirror coverage TRX files into the canonical test-results directory so downstream
            // GitHub Actions reporters (dorny/test-reporter, upload-artifact) and the per-OS
            // workflow steps all see results in the same location regardless of host. Previously
            // only macOS had an OS-specific copy step; Linux silently produced zero TRX matches.
            foreach (var trx in CoverageDirectory.GlobFiles("**/*.trx"))
            {
                File.Copy(trx, TestResultsDirectory / trx.Name, overwrite: true);
            }

            var coverageFiles = CoverageDirectory.GlobFiles("**/coverage.cobertura.xml");
            if (coverageFiles.Count == 0)
            {
                Assert.Fail("No coverage files found. Ensure coverlet.collector is referenced in the test project.");
            }

            var coverageFile = coverageFiles.First();

            DotNet(
                $"reportgenerator " +
                $"\"-reports:{coverageFile}\" " +
                $"\"-targetdir:{CoverageReportDirectory}\" " +
                $"\"-reporttypes:Html;Cobertura;TextSummary\" " +
                $"\"-assemblyfilters:+Agibuild.Fulora.*;-Agibuild.Fulora.Testing;-Agibuild.Fulora.UnitTests;-Agibuild.Fulora.Platforms.UnitTests\"",
                workingDirectory: RootDirectory);

            var mergedCoberturaFile = CoverageReportDirectory / "Cobertura.xml";
            var coberturaPath = File.Exists(mergedCoberturaFile) ? mergedCoberturaFile : coverageFile;
            var lineCoveragePct = ReadCoberturaLineCoveragePercent(coberturaPath);
            var branchCoveragePct = ReadCoberturaBranchCoveragePercent(coberturaPath);
            Serilog.Log.Information("Line coverage: {Coverage:F2}% (threshold: {Threshold}%)", lineCoveragePct, CoverageThreshold);
            Serilog.Log.Information("Branch coverage: {Coverage:F2}% (threshold: {Threshold}%)", branchCoveragePct, BranchCoverageThreshold);
            Serilog.Log.Information("HTML report: {Path}", CoverageReportDirectory / "index.html");

            if (lineCoveragePct < CoverageThreshold)
            {
                Assert.Fail(
                    $"Line coverage {lineCoveragePct:F2}% is below the required threshold of {CoverageThreshold}%. " +
                    $"Review the report at {CoverageReportDirectory / "index.html"}");
            }

            if (branchCoveragePct < BranchCoverageThreshold)
            {
                Assert.Fail(
                    $"Branch coverage {branchCoveragePct:F2}% is below the required threshold of {BranchCoverageThreshold}%. " +
                    $"Review the report at {CoverageReportDirectory / "index.html"}");
            }

            Serilog.Log.Information(
                "Coverage gate PASSED: line {LineCoverage:F2}% >= {LineThreshold}%, branch {BranchCoverage:F2}% >= {BranchThreshold}%",
                lineCoveragePct, CoverageThreshold, branchCoveragePct, BranchCoverageThreshold);

            var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
            if (!string.IsNullOrEmpty(summaryPath))
            {
                var textSummaryFile = CoverageReportDirectory / "Summary.txt";
                var summaryContent = File.Exists(textSummaryFile)
                    ? File.ReadAllText(textSummaryFile)
                    : $"Line coverage: {lineCoveragePct:F2}%, Branch coverage: {branchCoveragePct:F2}%";

                var markdown =
                    $"## Code Coverage Report\n\n" +
                    $"| Metric | Value |\n" +
                    $"|--------|-------|\n" +
                    $"| **Line Coverage** | **{lineCoveragePct:F2}%** |\n" +
                    $"| **Branch Coverage** | **{branchCoveragePct:F2}%** |\n" +
                    $"| Line Threshold | {CoverageThreshold}% |\n" +
                    $"| Branch Threshold | {BranchCoverageThreshold}% |\n" +
                    $"| Status | {(lineCoveragePct >= CoverageThreshold && branchCoveragePct >= BranchCoverageThreshold ? "PASSED" : "FAILED")} |\n\n" +
                    $"<details><summary>Full Summary</summary>\n\n```\n{summaryContent}\n```\n\n</details>\n";

                File.AppendAllText(summaryPath, markdown);
            }
        });

    internal Target IntegrationTests => _ => _
        .Description("Runs automated integration tests.")
        .DependsOn(Build)
        .Executes(() =>
        {
            DotNetTest(s => s
                .SetProjectFile(IntegrationTestsProject)
                .SetConfiguration(Configuration)
                .EnableNoRestore()
                .EnableNoBuild()
                .SetResultsDirectory(TestResultsDirectory)
                .SetLoggers("trx;LogFileName=integration-tests.trx"));
        });

    private static IReadOnlyList<AbsolutePath> GetMutationProjectsToBuild() =>
    [
        CoreProject,
        RuntimeProject,
        SrcDirectory / "Agibuild.Fulora.AI" / "Agibuild.Fulora.AI.csproj",
        UnitTestsProject
    ];

    internal Target BuildMutationScope => _ => _
        .Description("Builds only projects required by mutation testing profiles.")
        .DependsOn(Clean)
        .Executes(() =>
        {
            foreach (var project in GetMutationProjectsToBuild())
            {
                DotNetRestore(s => s
                    .SetProjectFile(project));

                DotNetBuild(s => s
                    .SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .EnableNoRestore());
            }
        });

    internal Target ContractAutomation => _ => _
        .Description("Runs ContractAutomation lane (mock-driven unit tests).")
        .DependsOn(Build)
        .Executes(async () =>
        {
            await RunContractAutomationTests("contract-automation.trx");
        });

    internal Target RuntimeAutomation => _ => _
        .Description("Runs RuntimeAutomation lane (real adapter/runtime automation tests).")
        .DependsOn(Build)
        .Executes(async () =>
        {
            await RunRuntimeAutomationTests("runtime-automation.trx");
        });

    internal Target AutomationLaneReport => _ => _
        .Description("Runs automation lanes and writes pass/fail/skip report.")
        .DependsOn(Build, Coverage)
        .Executes(async () =>
        {
            var lanes = new List<AutomationLaneResult>();
            var failures = new List<string>();

            // ContractAutomation == the unit-test suite that Coverage already ran. Re-running the
            // same ~2k tests a second time turned every macOS CI run into an 8-minute timeout
            // marathon (4-min default × 2 attempts) without adding any signal: if Coverage passed,
            // contract automation passed by definition. Derive the lane status from Coverage's TRX
            // so the report stays accurate while CI does the work exactly once.
            var coverageTrx = CoverageDirectory.GlobFiles("**/unit-tests.trx").FirstOrDefault();
            if (coverageTrx is null)
            {
                Assert.Fail(
                    $"ContractAutomation lane requires Coverage to have produced unit-tests.trx in " +
                    $"{CoverageDirectory}. Ensure the Coverage target ran successfully before this target.");
            }

            RecordLaneFromTrx(
                lane: ContractAutomationLane,
                project: UnitTestsProject,
                trxFile: coverageTrx!,
                lanes,
                failures);

            await RunLaneWithReportingAsync(
                lane: RuntimeAutomationLane,
                project: IntegrationTestsProject,
                run: () => RunRuntimeAutomationTests("runtime-automation.trx"),
                lanes,
                failures);

            if (!OperatingSystem.IsMacOS())
            {
                lanes.Add(new AutomationLaneResult(
                    Lane: $"{RuntimeAutomationLane}.iOS",
                    Status: "skipped",
                    Project: E2EiOSProject.ToString(),
                    Reason: "Requires macOS host with iOS simulator tooling."));
            }
            else if (!await HasDotNetWorkloadAsync("ios"))
            {
                lanes.Add(new AutomationLaneResult(
                    Lane: $"{RuntimeAutomationLane}.iOS",
                    Status: "skipped",
                    Project: E2EiOSProject.ToString(),
                    Reason: "iOS workload not installed."));
            }

            if (!await HasDotNetWorkloadAsync("android"))
            {
                lanes.Add(new AutomationLaneResult(
                    Lane: $"{RuntimeAutomationLane}.Android",
                    Status: "skipped",
                    Project: E2EAndroidProject.ToString(),
                    Reason: "Android workload not installed."));
            }

            if (!OperatingSystem.IsLinux())
            {
                lanes.Add(new AutomationLaneResult(
                    Lane: $"{RuntimeAutomationLane}.Gtk",
                    Status: "skipped",
                    Project: E2EDesktopProject.ToString(),
                    Reason: "Requires Linux host with WebKitGTK and a display server (Xvfb on CI)."));
            }
            else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
                     && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                lanes.Add(new AutomationLaneResult(
                    Lane: $"{RuntimeAutomationLane}.Gtk",
                    Status: "skipped",
                    Project: E2EDesktopProject.ToString(),
                    Reason: "No DISPLAY/WAYLAND_DISPLAY detected; cannot run GTK smoke without a display server."));
            }
            else
            {
                await RunLaneWithReportingAsync(
                    lane: $"{RuntimeAutomationLane}.Gtk",
                    project: E2EDesktopProject,
                    run: RunGtkSmokeDesktopAppAsync,
                    lanes,
                    failures);
            }

            TestResultsDirectory.CreateDirectory();
            var laneManifestExists = File.Exists(AutomationLaneManifestFile);
            var reportPayload = new
            {
                generatedAtUtc = DateTime.UtcNow,
                laneManifestPath = AutomationLaneManifestFile.ToString(),
                laneManifestExists,
                lanes
            };

            WriteJsonReport(AutomationLaneReportFile, reportPayload);
            Serilog.Log.Information("Automation lane report written to {Path}", AutomationLaneReportFile);

            if (failures.Count > 0)
            {
                Assert.Fail("Automation lane failures:\n" + string.Join('\n', failures));
            }
        });

    internal Target Test => _ => _
        .Description("Runs all tests (unit + integration).")
        .DependsOn(UnitTests, IntegrationTests);

    private static AbsolutePath MutationReportDirectory => ArtifactsDirectory / "mutation-report";
    private sealed record MutationTestProfile(string Name, string ConfigFileName);

    private static IReadOnlyList<MutationTestProfile> GetMutationProfiles() =>
    [
        new("core", "stryker-config.core.json"),
        new("runtime", "stryker-config.runtime.json"),
        new("ai", "stryker-config.ai.json")
    ];

    private IReadOnlyList<MutationTestProfile> ResolveMutationProfilesToRun()
    {
        var profiles = GetMutationProfiles();
        if (string.IsNullOrWhiteSpace(MutationProfile))
            return profiles;

        var selectedProfiles = profiles
            .Where(profile => string.Equals(profile.Name, MutationProfile.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (selectedProfiles.Count > 0)
            return selectedProfiles;

        var allowedProfiles = string.Join(", ", profiles.Select(profile => profile.Name));
        Assert.Fail($"Unknown mutation profile '{MutationProfile}'. Allowed values: {allowedProfiles}");
        return profiles;
    }

    private static void AppendMutationProgressSummary(
        IReadOnlyList<(string Name, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, TimeSpan Elapsed, AbsolutePath OutputPath)> profileRuns)
    {
        if (profileRuns.Count == 0)
            return;

        var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(summaryPath))
            return;

        var lines = new List<string>
        {
            "## Mutation Profile Progress",
            string.Empty,
            "| Profile | Started (UTC) | Ended (UTC) | Elapsed | Report Path |",
            "|---|---|---|---|---|"
        };

        lines.AddRange(profileRuns.Select(run =>
            $"| `{run.Name}` | `{run.StartedAtUtc:O}` | `{run.EndedAtUtc:O}` | `{run.Elapsed:c}` | `{run.OutputPath}` |"));
        lines.Add(string.Empty);

        File.AppendAllText(summaryPath, string.Join(Environment.NewLine, lines));
    }

    internal Target MutationTest => _ => _
        .Description("Runs Stryker.NET mutation testing on core business profiles.")
        .DependsOn(BuildMutationScope)
        .Executes(() =>
        {
            MutationReportDirectory.CreateOrCleanDirectory();
            var selectedProfiles = ResolveMutationProfilesToRun();
            var selectedProfileNames = string.Join(", ", selectedProfiles.Select(profile => profile.Name));
            Serilog.Log.Information("Mutation profiles selected: {Profiles}", selectedProfileNames);
            var profileRuns = new List<(string Name, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, TimeSpan Elapsed, AbsolutePath OutputPath)>();

            foreach (var profile in selectedProfiles)
            {
                var configPath = RootDirectory / profile.ConfigFileName;
                Assert.FileExists(configPath, $"Missing mutation profile config: {configPath}");

                var profileOutput = MutationReportDirectory / profile.Name;
                profileOutput.CreateOrCleanDirectory();
                var startedAtUtc = DateTimeOffset.UtcNow;
                Serilog.Log.Information("Mutation profile '{Profile}' started at {StartedAtUtc:O}", profile.Name, startedAtUtc);

                DotNet(
                    $"stryker --config-file {configPath} --output {profileOutput} --log-to-file",
                    workingDirectory: UnitTestsProject.Parent);

                var endedAtUtc = DateTimeOffset.UtcNow;
                var elapsed = endedAtUtc - startedAtUtc;
                Serilog.Log.Information(
                    "Mutation profile '{Profile}' completed at {EndedAtUtc:O} (elapsed: {Elapsed:c}). Report: {ReportPath}",
                    profile.Name,
                    endedAtUtc,
                    elapsed,
                    profileOutput);
                profileRuns.Add((profile.Name, startedAtUtc, endedAtUtc, elapsed, profileOutput));
            }

            AppendMutationProgressSummary(profileRuns);
            Serilog.Log.Information("Mutation report: {Path}", MutationReportDirectory);
        });

    internal Target E2ETests => _ => _
        .DependsOn(Build)
        .Description("Run E2E integration tests (platform-gated, requires real WebView adapter)")
        .OnlyWhenDynamic(() => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
        .Executes(() =>
        {
            Serilog.Log.Information("E2E tests require a real WebView adapter — skipped in headless CI by default");
            Serilog.Log.Information("To run E2E tests locally: dotnet test tests/Agibuild.Fulora.Testing --filter Category=E2E");
        });
}
