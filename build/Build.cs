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
internal sealed partial class BuildTask : NukeBuild
{
    private const string ContractAutomationLane = "ContractAutomation";
    private const string RuntimeAutomationLane = "RuntimeAutomation";
    private const string LaneContextCi = "Ci";
    private const string LaneContextCiPublish = "CiPublish";
    private const string PrimaryHostPackageId = "Agibuild.Fulora.Avalonia";

    private sealed record AutomationLaneResult(
        string Lane,
        string Status,
        string Project,
        string? Reason = null);

    public static int Main() => Execute<BuildTask>(x => x.Build);

    // ──────────────────────────────── Parameters ────────────────────────────────

    [Parameter("Configuration (Debug / Release). Default: Release on CI, Debug locally.")]
    private readonly string Configuration = IsServerBuild ? "Release" : "Debug";

    [Parameter("Version suffix for pack/publish targets (e.g., ci.42, rc.1). Empty = stable release.")]
    private readonly string? VersionSuffix = null;

    [Parameter("NuGet source URL for publish. Default: https://api.nuget.org/v3/index.json")]
    private readonly string NuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("NuGet API key for publish.")]
    [Secret]
    private readonly string? NuGetApiKey = Environment.GetEnvironmentVariable("NUGET_API_KEY");

    [Parameter("npm auth token for @agibuild/fulora-client publication.")]
    [Secret]
    private readonly string? NpmToken = Environment.GetEnvironmentVariable("NPM_TOKEN");

    [Parameter("Minimum line coverage percentage (0-100). Default: 96")]
    private readonly int CoverageThreshold = 96;

    [Parameter("Minimum branch coverage percentage (0-100). Default: 93")]
    private readonly int BranchCoverageThreshold = 93;

    [Parameter("Android AVD name for emulator. Default: auto-detect first available AVD.")]
    private readonly string? AndroidAvd = null;

    [Parameter("iOS Simulator device name. Default: auto-detect first available iPhone simulator.")]
    private readonly string? IosSimulator = null;

    [Parameter("Android SDK root path. Default: ~/Library/Android/sdk (macOS) or ANDROID_HOME env var.")]
    private readonly string AndroidSdkRoot = ResolveAndroidSdkRoot();

    [Parameter("Project to package (path to .csproj)")]
    private readonly string? PackageProject = null;

    [Parameter("Target runtime identifier (e.g., win-x64, osx-arm64)")]
    private readonly string? PackageRuntime = null;

    [Parameter("Application version for packaging")]
    private readonly string? PackageAppVersion = null;

    [Parameter("Output directory for packaged app")]
    private readonly string? PackageOutput = null;

    [Parameter("Optional mutation profile filter (core/runtime/ai). Empty = all profiles.")]
    private readonly string? MutationProfile = null;

    private static string ResolveAndroidSdkRoot()
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

    private static AbsolutePath SrcDirectory => RootDirectory / "src";
    private static AbsolutePath TestsDirectory => RootDirectory / "tests";
    private static AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    private static AbsolutePath PackageOutputDirectory => ArtifactsDirectory / "packages";
    private static AbsolutePath TestResultsDirectory => ArtifactsDirectory / "test-results";
    private static AbsolutePath AutomationLaneReportFile => TestResultsDirectory / "automation-lane-report.json";
    private static AbsolutePath NugetSmokeTelemetryFile => TestResultsDirectory / "nuget-smoke-retry-telemetry.json";
    private static AbsolutePath AutomationLaneManifestFile => TestsDirectory / "automation-lanes.json";

    private static AbsolutePath SolutionFile => RootDirectory / "Agibuild.Fulora.slnx";
    private static AbsolutePath CoverageDirectory => ArtifactsDirectory / "coverage";
    private static AbsolutePath CoverageReportDirectory => ArtifactsDirectory / "coverage-report";

    private static AbsolutePath PackProject =>
        SrcDirectory / "Agibuild.Fulora.Avalonia" / "Agibuild.Fulora.Avalonia.csproj";

    private static AbsolutePath UnitTestsProject =>
        TestsDirectory / "Agibuild.Fulora.UnitTests" / "Agibuild.Fulora.UnitTests.csproj";

    private static AbsolutePath CliUnitTestsProject =>
        TestsDirectory / "Agibuild.Fulora.Cli.UnitTests" / "Agibuild.Fulora.Cli.UnitTests.csproj";

    private static AbsolutePath PlatformsUnitTestsProject =>
        TestsDirectory / "Agibuild.Fulora.Platforms.UnitTests" / "Agibuild.Fulora.Platforms.UnitTests.csproj";

    private static AbsolutePath IntegrationTestsProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.Tests.Automation"
        / "Agibuild.Fulora.Integration.Tests.Automation.csproj";

    private static AbsolutePath E2EDesktopProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.Tests"
        / "Agibuild.Fulora.Integration.Tests.Desktop"
        / "Agibuild.Fulora.Integration.Tests.Desktop.csproj";

    private static AbsolutePath E2EAndroidProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.Tests"
        / "Agibuild.Fulora.Integration.Tests.Android"
        / "Agibuild.Fulora.Integration.Tests.Android.csproj";

    private static AbsolutePath E2EiOSProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.Tests"
        / "Agibuild.Fulora.Integration.Tests.iOS"
        / "Agibuild.Fulora.Integration.Tests.iOS.csproj";

    private static AbsolutePath NugetPackageTestProject =>
        TestsDirectory / "Agibuild.Fulora.Integration.NugetPackageTests"
        / "Agibuild.Fulora.Integration.NugetPackageTests.csproj";

    private static AbsolutePath CoreProject =>
        SrcDirectory / "Agibuild.Fulora.Core" / "Agibuild.Fulora.Core.csproj";

    private static AbsolutePath BridgeGeneratorProject =>
        SrcDirectory / "Agibuild.Fulora.Bridge.Generator" / "Agibuild.Fulora.Bridge.Generator.csproj";

    private static AbsolutePath AdaptersAbstractionsProject =>
        SrcDirectory / "Agibuild.Fulora.Adapters.Abstractions" / "Agibuild.Fulora.Adapters.Abstractions.csproj";

    private static AbsolutePath RuntimeProject =>
        SrcDirectory / "Agibuild.Fulora.Runtime" / "Agibuild.Fulora.Runtime.csproj";

    private static AbsolutePath DependencyInjectionProject =>
        SrcDirectory / "Agibuild.Fulora.DependencyInjection" / "Agibuild.Fulora.DependencyInjection.csproj";

    private static AbsolutePath PlatformsProject =>
        SrcDirectory / "Agibuild.Fulora.Platforms" / "Agibuild.Fulora.Platforms.csproj";

    private static AbsolutePath TestingProject =>
        TestsDirectory / "Agibuild.Fulora.Testing" / "Agibuild.Fulora.Testing.csproj";

    private static AbsolutePath CliProject =>
        SrcDirectory / "Agibuild.Fulora.Cli" / "Agibuild.Fulora.Cli.csproj";

    private static AbsolutePath PluginLocalStorageProject =>
        RootDirectory / "plugins" / "Agibuild.Fulora.Plugin.LocalStorage" / "Agibuild.Fulora.Plugin.LocalStorage.csproj";

    private static AbsolutePath PluginAuthTokenProject =>
        RootDirectory / "plugins" / "Agibuild.Fulora.Plugin.AuthToken" / "Agibuild.Fulora.Plugin.AuthToken.csproj";

    private static AbsolutePath PluginHttpClientProject =>
        RootDirectory / "plugins" / "Agibuild.Fulora.Plugin.HttpClient" / "Agibuild.Fulora.Plugin.HttpClient.csproj";

    private static AbsolutePath PluginDatabaseProject =>
        RootDirectory / "plugins" / "Agibuild.Fulora.Plugin.Database" / "Agibuild.Fulora.Plugin.Database.csproj";

    private static AbsolutePath PluginFileSystemProject =>
        RootDirectory / "plugins" / "Agibuild.Fulora.Plugin.FileSystem" / "Agibuild.Fulora.Plugin.FileSystem.csproj";

    private static AbsolutePath PluginNotificationsProject =>
        RootDirectory / "plugins" / "Agibuild.Fulora.Plugin.Notifications" / "Agibuild.Fulora.Plugin.Notifications.csproj";

    private static AbsolutePath OpenTelemetryProject =>
        SrcDirectory / "Agibuild.Fulora.Telemetry.OpenTelemetry" / "Agibuild.Fulora.Telemetry.OpenTelemetry.csproj";

    private static AbsolutePath TemplatePackProject =>
        RootDirectory / "templates" / "Agibuild.Fulora.Templates.csproj";

    private static AbsolutePath TemplatePath =>
        RootDirectory / "templates" / "agibuild-hybrid";

    private static AbsolutePath ReactSampleDirectory => RootDirectory / "samples" / "avalonia-react";
    private static AbsolutePath ReactWebDirectory => ReactSampleDirectory / "AvaloniReact.Web";
    private static AbsolutePath ReactDesktopProject => ReactSampleDirectory / "AvaloniReact.Desktop" / "AvaloniReact.Desktop.csproj";

    private static AbsolutePath AiChatSampleDirectory => RootDirectory / "samples" / "avalonia-ai-chat";
    private static AbsolutePath AiChatWebDirectory => AiChatSampleDirectory / "AvaloniAiChat.Web";
    private static AbsolutePath AiChatDesktopProject => AiChatSampleDirectory / "AvaloniAiChat.Desktop" / "AvaloniAiChat.Desktop.csproj";

    private static AbsolutePath VueSampleDirectory => RootDirectory / "samples" / "avalonia-vue";
    private static AbsolutePath VueWebDirectory => VueSampleDirectory / "AvaloniVue.Web";
    private static AbsolutePath VueDesktopProject => VueSampleDirectory / "AvaloniVue.Desktop" / "AvaloniVue.Desktop.csproj";

    private static AbsolutePath TodoSampleDirectory => RootDirectory / "samples" / "showcase-todo";
    private static AbsolutePath TodoWebDirectory => TodoSampleDirectory / "ShowcaseTodo.Web";
    private static AbsolutePath TodoDesktopProject => TodoSampleDirectory / "ShowcaseTodo.Desktop" / "ShowcaseTodo.Desktop.csproj";

    private static AbsolutePath MinimalHybridDesktopProject => RootDirectory / "samples" / "minimal-hybrid" / "MinimalHybrid.Desktop" / "MinimalHybrid.Desktop.csproj";

    // ──────────────────────────────── Core Lifecycle ────────────────────────────────────

    internal Target Clean => _ => _
        .Description("Cleans bin/obj directories and the artifacts folder.")
        .Executes(() =>
        {
            SrcDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => d.DeleteDirectory());
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => d.DeleteDirectory());
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    // Restore is intentionally NOT dependent on Clean. `dotnet restore` is incremental, CI runners
    // start from a clean checkout, and chaining Clean here causes any subsequent Nuke invocation in
    // the same job (e.g. Format → CiMatrix → ValidateLinuxNativeAotNugetPackagePublish on Linux CI)
    // to wipe `artifacts/test-results/` before the workflow can upload it. Clean is a lifecycle
    // target — invoke it explicitly when a full rebuild is wanted.
    internal Target Restore => _ => _
        .Description("Restores NuGet packages for buildable projects (avoids failing on missing workloads).")
        .Executes(async () =>
        {
            // Mirror the Build target's android-slice gating so restore on hosts without the
            // Android workload doesn't fail at NETSDK1147 when restoring Platforms.csproj.
            var skipAndroidSlice = !await HasDotNetWorkloadAsync("android") || !HasAndroidSdkInstalled();

            foreach (var project in await GetProjectsToBuildAsync())
            {
                var isPlatforms = string.Equals(
                    Path.GetFileName(project),
                    "Agibuild.Fulora.Platforms.csproj",
                    StringComparison.OrdinalIgnoreCase);

                DotNetRestore(s =>
                {
                    var settings = s.SetProjectFile(project);

                    if (isPlatforms && skipAndroidSlice)
                    {
                        settings = settings.SetProperty("EnableAndroidTfm", "false");
                    }

                    return settings;
                });
            }
        });

    internal Target Build => _ => _
        .Description("Builds all platform-appropriate projects.")
        .DependsOn(Restore)
        .Executes(async () =>
        {
            // Multi-TFM Platforms.csproj exposes net10.0 + net10.0-android. When the Android workload
            // is missing, force-build only the net10.0 slice so the missing workload doesn't fail the build.
            var skipAndroidSlice = !await HasDotNetWorkloadAsync("android") || !HasAndroidSdkInstalled();

            foreach (var project in await GetProjectsToBuildAsync())
            {
                var projectFileName = Path.GetFileName(project);
                var isPlatforms = string.Equals(
                    projectFileName,
                    "Agibuild.Fulora.Platforms.csproj",
                    StringComparison.OrdinalIgnoreCase);
                DotNetBuild(s =>
                {
                    var settings = s
                        .SetProjectFile(project)
                        .SetConfiguration(Configuration)
                        .EnableNoRestore();

                    if (isPlatforms && skipAndroidSlice)
                    {
                        settings = settings.SetProperty("EnableAndroidTfm", "false");
                    }

                    return settings;
                });
            }
        });

    internal Target BuildAll => _ => _
        .Description("Builds all platform-compatible projects via solution filter. Use for CodeQL / full analysis.")
        .DependsOn(Clean)
        .Executes(async () =>
        {
            var filterPath = await BuildPlatformAwareSolutionFilterAsync("build-all");
            DotNet($"build {filterPath} --configuration {Configuration}",
                   workingDirectory: RootDirectory);
        });

    // ──────────────────────────────── CI Targets ─────────────────────────────

    internal Target Ci => _ => _
        .Description("Full CI pipeline: compile → coverage → lane automation → validate package → pack.")
        .DependsOn(Coverage, MaciosUnitTests, AutomationLaneReport, ValidatePackage, NugetPackageTest, PackTemplate);

    internal Target CiMatrix => _ => _
        .Description("Cross-platform CI validation without package smoke/template packing.")
        .DependsOn(Coverage, AutomationLaneReport, ValidatePackage);

    internal Target CiPublish => _ => _
        .Description("Full release pipeline: Ci + publish.")
        .DependsOn(Ci, Publish);
}
