using System;
using System.IO;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "ci",
    GitHubActionsImage.MacOsLatest,
    On = [GitHubActionsTrigger.Push, GitHubActionsTrigger.PullRequest],
    InvokedTargets = [nameof(Ci)],
    AutoGenerate = false)]
partial class BuildTask : NukeBuild
{
    private const string ContractAutomationLane = "ContractAutomation";
    private const string RuntimeAutomationLane = "RuntimeAutomation";

    private sealed record AutomationLaneResult(
        string Lane,
        string Status,
        string Project,
        string? Reason = null);

    public static int Main() => Execute<BuildTask>(x => x.Build);

    // ──────────────────────────────── Parameters ────────────────────────────────

    [Parameter("Configuration (Debug / Release). Default: Release on CI, Debug locally.")]
    readonly string Configuration = IsServerBuild ? "Release" : "Debug";

    [Parameter("NuGet package version override. When set, overrides MinVer auto-calculated version.")]
    readonly string? PackageVersion = null;

    [Parameter("NuGet source URL for publish. Default: https://api.nuget.org/v3/index.json")]
    readonly string NuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("NuGet API key for publish.")]
    [Secret]
    readonly string? NuGetApiKey = Environment.GetEnvironmentVariable("NUGET_API_KEY");

    [Parameter("npm auth token for @agibuild/bridge publication.")]
    [Secret]
    readonly string? NpmToken = Environment.GetEnvironmentVariable("NPM_TOKEN");

    [Parameter("Minimum line coverage percentage (0-100). Default: 96")]
    readonly int CoverageThreshold = 96;

    [Parameter("Minimum branch coverage percentage (0-100). Default: 94")]
    readonly int BranchCoverageThreshold = 94;

    [Parameter("Android AVD name for emulator. Default: auto-detect first available AVD.")]
    readonly string? AndroidAvd = null;

    [Parameter("iOS Simulator device name. Default: auto-detect first available iPhone simulator.")]
    readonly string? iOSSimulator = null;

    [Parameter("Android SDK root path. Default: ~/Library/Android/sdk (macOS) or ANDROID_HOME env var.")]
    readonly string AndroidSdkRoot = ResolveAndroidSdkRoot();

    [Parameter("Optional warning scanner input file path. If set, scanner consumes this file instead of live build output.")]
    readonly string? WarningGovernanceInput = null;

    static string ResolveAndroidSdkRoot()
    {
        var home = Environment.GetEnvironmentVariable("ANDROID_HOME");
        if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
            return home;

        var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrEmpty(sdkRoot) && Directory.Exists(sdkRoot))
            return sdkRoot;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "sdk");
    }

    // ──────────────────────────────── Paths ──────────────────────────────────────

    AbsolutePath SrcDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath PackageOutputDirectory => ArtifactsDirectory / "packages";
    AbsolutePath TestResultsDirectory => ArtifactsDirectory / "test-results";
    AbsolutePath AutomationLaneReportFile => TestResultsDirectory / "automation-lane-report.json";
    AbsolutePath NugetSmokeTelemetryFile => TestResultsDirectory / "nuget-smoke-retry-telemetry.json";
    AbsolutePath WarningGovernanceReportFile => TestResultsDirectory / "warning-governance-report.json";
    AbsolutePath OpenSpecStrictGovernanceReportFile => TestResultsDirectory / "openspec-strict-governance.log";
    AbsolutePath DependencyGovernanceReportFile => TestResultsDirectory / "dependency-governance-report.json";
    AbsolutePath TypeScriptGovernanceReportFile => TestResultsDirectory / "typescript-governance-report.json";
    AbsolutePath RuntimeCriticalPathGovernanceReportFile => TestResultsDirectory / "runtime-critical-path-governance-report.json";
    AbsolutePath CloseoutSnapshotFile => TestResultsDirectory / "closeout-snapshot.json";
    AbsolutePath BridgeDistributionGovernanceReportFile => TestResultsDirectory / "bridge-distribution-governance-report.json";
    AbsolutePath TransitionGateGovernanceReportFile => TestResultsDirectory / "transition-gate-governance-report.json";
    AbsolutePath ReleaseOrchestrationDecisionReportFile => TestResultsDirectory / "release-orchestration-decision-report.json";
    AbsolutePath DistributionReadinessGovernanceReportFile => TestResultsDirectory / "distribution-readiness-governance-report.json";
    AbsolutePath AdoptionReadinessGovernanceReportFile => TestResultsDirectory / "adoption-readiness-governance-report.json";
    AbsolutePath AutomationLaneManifestFile => TestsDirectory / "automation-lanes.json";
    AbsolutePath RuntimeCriticalPathManifestFile => TestsDirectory / "runtime-critical-path.manifest.json";
    AbsolutePath WarningGovernanceBaselineFile => TestsDirectory / "warning-governance.baseline.json";

    AbsolutePath SolutionFile => RootDirectory / "Agibuild.Fulora.sln";
    AbsolutePath CoverageDirectory => ArtifactsDirectory / "coverage";
    AbsolutePath CoverageReportDirectory => ArtifactsDirectory / "coverage-report";

    AbsolutePath PackProject =>
        SrcDirectory / "Agibuild.Fulora.Avalonia" / "Agibuild.Fulora.Avalonia.csproj";

    AbsolutePath UnitTestsProject =>
        TestsDirectory / "Agibuild.Fulora.UnitTests" / "Agibuild.Fulora.UnitTests.csproj";

    AbsolutePath IntegrationTestsProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.Tests.Automation"
        / "Agibuild.Fulora.Integration.Tests.Automation.csproj";

    AbsolutePath E2EDesktopProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.Tests"
        / "Agibuild.Fulora.Integration.Tests.Desktop"
        / "Agibuild.Fulora.Integration.Tests.Desktop.csproj";

    AbsolutePath E2EAndroidProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.Tests"
        / "Agibuild.Fulora.Integration.Tests.Android"
        / "Agibuild.Fulora.Integration.Tests.Android.csproj";

    AbsolutePath E2EiOSProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.Tests"
        / "Agibuild.Fulora.Integration.Tests.iOS"
        / "Agibuild.Fulora.Integration.Tests.iOS.csproj";

    AbsolutePath NugetPackageTestProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.NugetPackageTests"
        / "Agibuild.Fulora.Integration.NugetPackageTests.csproj";

    AbsolutePath CoreProject =>
        SrcDirectory / "Agibuild.Fulora.Core" / "Agibuild.Fulora.Core.csproj";

    AbsolutePath BridgeGeneratorProject =>
        SrcDirectory / "Agibuild.Fulora.Bridge.Generator" / "Agibuild.Fulora.Bridge.Generator.csproj";

    AbsolutePath AdaptersAbstractionsProject =>
        SrcDirectory / "Agibuild.Fulora.Adapters.Abstractions" / "Agibuild.Fulora.Adapters.Abstractions.csproj";

    AbsolutePath RuntimeProject =>
        SrcDirectory / "Agibuild.Fulora.Runtime" / "Agibuild.Fulora.Runtime.csproj";

    AbsolutePath WindowsAdapterProject =>
        SrcDirectory / "Agibuild.Fulora.Adapters.Windows" / "Agibuild.Fulora.Adapters.Windows.csproj";

    AbsolutePath TestingProject =>
        TestsDirectory / "Agibuild.Fulora.Testing" / "Agibuild.Fulora.Testing.csproj";

    AbsolutePath CliProject =>
        SrcDirectory / "Agibuild.Fulora.Cli" / "Agibuild.Fulora.Cli.csproj";

    AbsolutePath PluginLocalStorageProject =>
        RootDirectory / "plugins" / "Agibuild.Fulora.Plugin.LocalStorage" / "Agibuild.Fulora.Plugin.LocalStorage.csproj";

    AbsolutePath TemplatePackProject =>
        RootDirectory / "templates" / "Agibuild.Fulora.Templates.csproj";

    AbsolutePath TemplatePath =>
        RootDirectory / "templates" / "agibuild-hybrid";

    AbsolutePath ReactSampleDirectory => RootDirectory / "samples" / "avalonia-react";
    AbsolutePath ReactWebDirectory => ReactSampleDirectory / "AvaloniReact.Web";
    AbsolutePath ReactDesktopProject => ReactSampleDirectory / "AvaloniReact.Desktop" / "AvaloniReact.Desktop.csproj";

    AbsolutePath VueSampleDirectory => RootDirectory / "samples" / "avalonia-vue";
    AbsolutePath VueWebDirectory => VueSampleDirectory / "AvaloniVue.Web";
    AbsolutePath VueDesktopProject => VueSampleDirectory / "AvaloniVue.Desktop" / "AvaloniVue.Desktop.csproj";

    // ──────────────────────────────── Core Lifecycle ────────────────────────────────────

    Target Clean => _ => _
        .Description("Cleans bin/obj directories and the artifacts folder.")
        .Executes(() =>
        {
            SrcDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => d.DeleteDirectory());
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => d.DeleteDirectory());
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Description("Restores NuGet packages for buildable projects (avoids failing on missing workloads).")
        .DependsOn(Clean)
        .Executes(() =>
        {
            foreach (var project in GetProjectsToBuild())
            {
                DotNetRestore(s => s
                    .SetProjectFile(project));
            }
        });

    Target Build => _ => _
        .Description("Builds all platform-appropriate projects.")
        .DependsOn(Restore)
        .Executes(() =>
        {
            foreach (var project in GetProjectsToBuild())
            {
                DotNetBuild(s => s
                    .SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .EnableNoRestore());
            }
        });

    // ──────────────────────────────── CI Targets ─────────────────────────────

    Target Ci => _ => _
        .Description("Full CI pipeline: compile → coverage → lane automation → pack → validate.")
        .DependsOn(Coverage, AutomationLaneReport, RuntimeCriticalPathExecutionGovernanceCi, WarningGovernance, DependencyVulnerabilityGovernance, TypeScriptDeclarationGovernance, OpenSpecStrictGovernance, ReleaseCloseoutSnapshot, ContinuousTransitionGateGovernance, AdoptionReadinessGovernanceCi, ValidatePackage);

    Target CiPublish => _ => _
        .Description("Full release pipeline: compile → coverage → lane automation → package smoke → publish.")
        .DependsOn(Coverage, AutomationLaneReport, NugetPackageTest, RuntimeCriticalPathExecutionGovernanceCiPublish, WarningGovernance, DependencyVulnerabilityGovernance, TypeScriptDeclarationGovernance, OpenSpecStrictGovernance, ReleaseCloseoutSnapshot, ContinuousTransitionGateGovernance, BridgeDistributionGovernance, DistributionReadinessGovernance, AdoptionReadinessGovernanceCiPublish, ReleaseOrchestrationGovernance, PackTemplate, Publish);
}
