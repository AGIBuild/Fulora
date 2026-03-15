using System.Text.Json;
using System.Text.RegularExpressions;
using Agibuild.Fulora.Testing;
using Xunit;
using static Agibuild.Fulora.Testing.GovernanceAssertionHelper;
using static Agibuild.Fulora.Testing.GovernanceInvariantIds;
using static Agibuild.Fulora.UnitTests.GovernanceSyntaxAssertionHelper;

namespace Agibuild.Fulora.UnitTests;

public sealed class AutomationLaneGovernanceTests
{
    [Fact]
    public void Automation_lane_manifest_declares_required_lanes_and_existing_projects()
    {
        var repoRoot = FindRepoRoot();
        var manifestPath = Path.Combine(repoRoot, "tests", "automation-lanes.json");

        using var doc = LoadJsonArtifact(manifestPath, AutomationLaneManifestSchema);
        var lanes = RequireProperty(doc.RootElement, "lanes", AutomationLaneManifestSchema, manifestPath);

        var laneNames = ExtractStringIds(lanes, "name");
        AssertContainsAll(laneNames, ["ContractAutomation", "RuntimeAutomation", "RuntimeAutomation.PackageSmoke"],
            AutomationLaneManifestSchema, manifestPath);

        foreach (var lane in lanes.EnumerateArray())
        {
            var project = lane.GetProperty("project").GetString()!;
            var projectPath = Path.Combine(repoRoot, project.Replace('/', Path.DirectorySeparatorChar));
            AssertFileExists(projectPath, AutomationLaneManifestSchema);
        }
    }

    [Fact]
    public void Runtime_critical_path_manifest_maps_to_existing_tests_or_targets()
    {
        var repoRoot = FindRepoRoot();
        var manifestPath = Path.Combine(repoRoot, "tests", "runtime-critical-path.manifest.json");

        using var doc = LoadJsonArtifact(manifestPath, RuntimeCriticalPathScenarioPresence);
        var scenarios = RequireProperty(doc.RootElement, "scenarios", RuntimeCriticalPathScenarioPresence, manifestPath);

        var requiredScenarioIds = new[]
        {
            "off-thread-handle-marshaling",
            "off-thread-navigation-marshaling",
            "lifecycle-contextmenu-reattach-wiring",
            "instance-options-isolation",
            "package-consumption-smoke",
            "shell-attach-detach-soak",
            "shell-multi-window-stress",
            "shell-host-capability-stress",
            "shell-product-experience-closure",
            "windows-webview2-teardown-stress",
            "shell-devtools-policy-isolation",
            "shell-devtools-lifecycle-cycles",
            "shell-shortcut-routing",
            "shell-system-integration-roundtrip",
            "shell-system-integration-v2-tray-payload",
            "shell-system-integration-v2-timestamp-normalization",
            "shell-system-integration-diagnostic-export"
        };

        var scenarioIds = ExtractStringIds(scenarios, "id");
        AssertContainsAll(scenarioIds, requiredScenarioIds, RuntimeCriticalPathScenarioPresence, manifestPath);

        var validCiContexts = new HashSet<string>(StringComparer.Ordinal) { "Ci" };

        foreach (var scenario in scenarios.EnumerateArray())
        {
            var id = scenario.GetProperty("id").GetString()!;
            var file = scenario.GetProperty("file").GetString()!;
            var testMethod = scenario.GetProperty("testMethod").GetString()!;
            var ciContext = scenario.TryGetProperty("ciContext", out var ciContextNode) ? ciContextNode.GetString()! : "Ci";

            AssertControlledVocabulary([ciContext], validCiContexts, RuntimeCriticalPathScenarioPresence, $"scenario '{id}' ciContext");

            AssertEvidenceLinkage(repoRoot, file, testMethod, RuntimeCriticalPathEvidenceLinkage);
        }
    }

    [Fact]
    public void System_integration_ct_matrix_contains_required_rows_and_machine_checkable_evidence()
    {
        var repoRoot = FindRepoRoot();
        var matrixPath = Path.Combine(repoRoot, "tests", "shell-system-integration-ct-matrix.json");

        using var doc = LoadJsonArtifact(matrixPath, SystemIntegrationCtMatrixSchema);
        var rows = RequireProperty(doc.RootElement, "rows", SystemIntegrationCtMatrixSchema, matrixPath);

        var rowIds = ExtractStringIds(rows, "id");
        AssertContainsAll(rowIds,
            ["tray-event-inbound", "menu-pruning", "system-action-whitelist", "tray-payload-v2-schema"],
            SystemIntegrationCtMatrixSchema, matrixPath);

        foreach (var row in rows.EnumerateArray())
        {
            var coverage = row.GetProperty("coverage").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.NotEmpty(coverage);

            AssertEvidenceItems(row.GetProperty("evidence"), repoRoot, SystemIntegrationCtMatrixSchema);
        }
    }

    [Fact]
    public void Build_pipeline_exposes_lane_targets_and_machine_readable_reports()
    {
        var repoRoot = FindRepoRoot();
        var combinedSource = ReadCombinedBuildSource(repoRoot);
        var mainSource = File.ReadAllText(Path.Combine(repoRoot, "build", "Build.cs"));

        var requiredTargets = new[]
        {
            "ContractAutomation", "RuntimeAutomation", "AutomationLaneReport",
            "WarningGovernance", "WarningGovernanceSyntheticCheck",
            "SampleTemplatePackageReferenceGovernance",
            "ReleaseCloseoutSnapshot", "DistributionReadinessGovernance",
            "AdoptionReadinessGovernance",
            "ReleaseOrchestrationGovernance"
        };
        foreach (var target in requiredTargets)
            AssertTargetDeclarationExists(combinedSource, target, BuildPipelineTargetGraph, "build/Build*.cs");

        var requiredArtifacts = new[]
        {
            "automation-lane-report.json", "warning-governance-report.json",
            "warning-governance.baseline.json", "nuget-smoke-retry-telemetry.json",
            "sample-template-package-reference-governance-report.json",
            "closeout-snapshot.json", "distribution-readiness-governance-report.json",
            "adoption-readiness-governance-report.json", "release-orchestration-decision-report.json"
        };
        foreach (var artifact in requiredArtifacts)
            AssertSourceContains(combinedSource, artifact, BuildPipelineTargetGraph, "build/Build*.cs");

        var requiredMethods = new[] { "RunNugetSmokeWithRetry", "ClassifyNugetSmokeFailure", "ResolveNugetPackagesRoot" };
        foreach (var method in requiredMethods)
            AssertSourceContains(combinedSource, method, BuildPipelineTargetGraph, "build/Build*.cs");

        AssertSourceContains(mainSource, "partial class BuildTask", BuildPipelineTargetGraph, "build/Build.cs");
        AssertSourceContains(mainSource, "Execute<BuildTask>(x => x.Build)", BuildPipelineTargetGraph, "build/Build.cs");
        AssertSourceContains(combinedSource, "--shellPreset app-shell", BuildPipelineTargetGraph, "build/Build*.cs");
    }

    [Fact]
    public void Warning_governance_baseline_disallows_windowsbase_entries()
    {
        var repoRoot = FindRepoRoot();
        var baselinePath = Path.Combine(repoRoot, "tests", "warning-governance.baseline.json");

        using var doc = LoadJsonArtifact(baselinePath, WarningGovernanceBaseline);
        var conflicts = RequireProperty(doc.RootElement, "windowsBaseConflicts", WarningGovernanceBaseline, baselinePath);
        Assert.Empty(conflicts.EnumerateArray().ToList());
    }

    [Fact]
    public void Webview2_reference_model_is_host_agnostic()
    {
        var repoRoot = FindRepoRoot();
        var adapterProjectPath = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Adapters.Windows", "Agibuild.Fulora.Adapters.Windows.csproj");
        var packProjectPath = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Avalonia", "Agibuild.Fulora.Avalonia.csproj");

        AssertFileExists(adapterProjectPath, WebView2ReferenceModel);
        AssertFileExists(packProjectPath, WebView2ReferenceModel);

        var adapterSource = File.ReadAllText(adapterProjectPath);
        var packSource = File.ReadAllText(packProjectPath);

        AssertSourceContains(adapterSource, "ExcludeAssets=\"compile;build;buildTransitive\"", WebView2ReferenceModel, adapterProjectPath);
        AssertSourceContains(adapterSource, "<Reference Include=\"Microsoft.Web.WebView2.Core\">", WebView2ReferenceModel, adapterProjectPath);
        Assert.DoesNotContain("MSB3277", adapterSource, StringComparison.Ordinal);
        AssertSourceContains(packSource, "ExcludeAssets=\"build;buildTransitive\"", WebView2ReferenceModel, packProjectPath);
    }

    [Fact]
    public void Core_runtime_and_adapter_abstractions_remain_host_neutral_without_avalonia_dependencies()
    {
        var repoRoot = FindRepoRoot();
        var governedProjects = new[]
        {
            ("src/Agibuild.Fulora.Core/Agibuild.Fulora.Core.csproj", "src/Agibuild.Fulora.Core"),
            ("src/Agibuild.Fulora.Runtime/Agibuild.Fulora.Runtime.csproj", "src/Agibuild.Fulora.Runtime"),
            ("src/Agibuild.Fulora.Adapters.Abstractions/Agibuild.Fulora.Adapters.Abstractions.csproj", "src/Agibuild.Fulora.Adapters.Abstractions")
        };

        foreach (var (projectRelPath, sourceRelPath) in governedProjects)
        {
            var projectPath = Path.Combine(repoRoot, projectRelPath.Replace('/', Path.DirectorySeparatorChar));
            AssertFileExists(projectPath, HostNeutralDependencyBoundary);
            var projectSource = File.ReadAllText(projectPath);
            Assert.DoesNotContain("PackageReference Include=\"Avalonia\"", projectSource, StringComparison.Ordinal);

            var sourceDir = Path.Combine(repoRoot, sourceRelPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(sourceDir), $"[{HostNeutralDependencyBoundary}] Missing source directory: {sourceDir}");

            foreach (var sourceFile in Directory.GetFiles(sourceDir, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(sourceFile);
                Assert.DoesNotContain("global::Avalonia.", source, StringComparison.Ordinal);
                Assert.DoesNotContain("using Avalonia.", source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Avalonia_host_bindings_are_isolated_to_host_layer_and_template_desktop_wiring_is_explicit()
    {
        var repoRoot = FindRepoRoot();

        var hostLayerSourceFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Agibuild.Fulora.Avalonia", "WebView.cs"),
            Path.Combine(repoRoot, "src", "Agibuild.Fulora.Avalonia", "AvaloniaWebDialog.cs"),
            Path.Combine(repoRoot, "src", "Agibuild.Fulora.Avalonia", "AppBuilderExtensions.cs")
        };
        foreach (var sourceFile in hostLayerSourceFiles)
            AssertFileExists(sourceFile, HostNeutralDependencyBoundary);

        var nonHostProjects = new[]
        {
            Path.Combine(repoRoot, "src", "Agibuild.Fulora.Core", "Agibuild.Fulora.Core.csproj"),
            Path.Combine(repoRoot, "src", "Agibuild.Fulora.Runtime", "Agibuild.Fulora.Runtime.csproj"),
            Path.Combine(repoRoot, "src", "Agibuild.Fulora.Adapters.Abstractions", "Agibuild.Fulora.Adapters.Abstractions.csproj")
        };
        foreach (var csproj in nonHostProjects)
        {
            AssertFileExists(csproj, HostNeutralDependencyBoundary);
            var source = File.ReadAllText(csproj);
            Assert.DoesNotContain("Avalonia", source, StringComparison.Ordinal);
        }

        var desktopProjectPath = Path.Combine(repoRoot, "templates", "agibuild-hybrid", "HybridApp.Desktop", "HybridApp.Desktop.csproj");
        AssertFileExists(desktopProjectPath, TemplateMetadataSchema);
        var desktopProject = File.ReadAllText(desktopProjectPath);

        AssertSourceContains(desktopProject, "<PackageReference Include=\"Agibuild.Fulora.Avalonia\"", TemplateMetadataSchema, desktopProjectPath);
        AssertSourceContains(desktopProject, "<PackageReference Include=\"Avalonia\"", TemplateMetadataSchema, desktopProjectPath);
        AssertSourceContains(desktopProject, "<PackageReference Include=\"Avalonia.Desktop\"", TemplateMetadataSchema, desktopProjectPath);
        Assert.DoesNotContain("<PackageReference Include=\"Agibuild.Fulora\"",
            desktopProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Agibuild.Fulora.Core", desktopProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Agibuild.Fulora.Runtime", desktopProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Agibuild.Fulora.Adapters.Abstractions", desktopProject, StringComparison.Ordinal);
    }

    [Fact]
    public void Xunit_v3_package_versions_are_aligned_across_repo_tests_templates_and_samples()
    {
        var repoRoot = FindRepoRoot();

        var projects = new[]
        {
            "tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj",
            "tests/Agibuild.Fulora.Integration.Tests.Automation/Agibuild.Fulora.Integration.Tests.Automation.csproj",
            "templates/agibuild-hybrid/HybridApp.Tests/HybridApp.Tests.csproj",
            "samples/avalonia-react/AvaloniReact.Tests/AvaloniReact.Tests.csproj",
        };

        var xunitV3Versions = new Dictionary<string, string>(StringComparer.Ordinal);
        var runnerVersions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var relative in projects)
        {
            var path = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            AssertFileExists(path, XunitVersionAlignment);
            var xml = File.ReadAllText(path);

            var xunitV3 = ExtractPackageVersion(xml, "xunit.v3");
            Assert.False(string.IsNullOrWhiteSpace(xunitV3), $"[{XunitVersionAlignment}] Missing xunit.v3 in {relative}");
            xunitV3Versions[relative] = xunitV3!;

            var runner = ExtractPackageVersion(xml, "xunit.runner.visualstudio");
            Assert.False(string.IsNullOrWhiteSpace(runner), $"[{XunitVersionAlignment}] Missing xunit.runner.visualstudio in {relative}");
            runnerVersions[relative] = runner!;
        }

        AssertSingleVersion("xunit.v3", xunitV3Versions);
        AssertSingleVersion("xunit.runner.visualstudio", runnerVersions);
    }

    [Fact]
    public void Hybrid_template_metadata_exposes_shell_preset_choices()
    {
        var repoRoot = FindRepoRoot();
        var templatePath = Path.Combine(repoRoot, "templates", "agibuild-hybrid", ".template.config", "template.json");

        using var doc = LoadJsonArtifact(templatePath, TemplateMetadataSchema);
        var symbols = RequireProperty(doc.RootElement, "symbols", TemplateMetadataSchema, templatePath);
        var shellPreset = RequireProperty(symbols, "shellPreset", TemplateMetadataSchema, templatePath);

        Assert.Equal("choice", shellPreset.GetProperty("datatype").GetString());
        Assert.Equal("app-shell", shellPreset.GetProperty("defaultValue").GetString());

        var choices = ExtractStringIds(shellPreset.GetProperty("choices"), "choice");
        AssertContainsAll(choices, ["baseline", "app-shell"], TemplateMetadataSchema, templatePath);
    }

    [Fact]
    public void Hybrid_template_source_contains_shell_preset_wiring_markers()
    {
        var repoRoot = FindRepoRoot();
        var basePath = Path.Combine(repoRoot, "templates", "agibuild-hybrid", "HybridApp.Desktop");

        var desktopMainWindowPath = Path.Combine(basePath, "MainWindow.axaml.cs");
        var appShellPresetPath = Path.Combine(basePath, "MainWindow.AppShellPreset.cs");
        var desktopProjectPath = Path.Combine(basePath, "HybridApp.Desktop.csproj");
        var desktopProgramPath = Path.Combine(basePath, "Program.cs");
        var desktopIndexPath = Path.Combine(basePath, "wwwroot", "index.html");

        foreach (var p in new[] { desktopMainWindowPath, appShellPresetPath, desktopProjectPath, desktopProgramPath, desktopIndexPath })
            AssertFileExists(p, TemplateMetadataSchema);

        var desktopMainWindow = File.ReadAllText(desktopMainWindowPath);
        var appShellPreset = File.ReadAllText(appShellPresetPath);
        var desktopProject = File.ReadAllText(desktopProjectPath);
        var desktopProgram = File.ReadAllText(desktopProgramPath);
        var desktopIndex = File.ReadAllText(desktopIndexPath);

        var mainWindowMarkers = new[]
        {
            "InitializeShellPreset();", "DisposeShellPreset();", "RegisterShellPresetBridgeServices();",
            "partial void InitializeShellPreset();", "partial void DisposeShellPreset();",
            "partial void RegisterShellPresetBridgeServices();"
        };
        foreach (var marker in mainWindowMarkers)
            AssertSourceContains(desktopMainWindow, marker, TemplateMetadataSchema, desktopMainWindowPath);

        var appShellPresetMarkers = new[]
        {
            "new WebViewShellExperience(", "new WebViewHostCapabilityBridge(",
            "WebView.Bridge.Expose<IDesktopHostService>", "TryHandleShellShortcutAsync",
            "ApplyMenuModel(", "UpdateTrayState(", "ExecuteSystemAction(",
            "_systemActionWhitelist = new HashSet<WebViewSystemAction>",
            "SystemActionWhitelist = _systemActionWhitelist",
            "ShowAbout remains disabled unless explicitly added",
            "ShowAbout opt-in snippet marker", "enableShowAboutAction",
            "IsShowAboutActionEnabledFromEnvironment", "AGIBUILD_TEMPLATE_ENABLE_SHOWABOUT",
            "SetShowAboutScenario", "GetSystemIntegrationStrategy",
            "template-showabout-policy-deny", "ShowAboutScenarioState",
            "canonical profile hash format",
            "SessionPermissionProfileResolver = new DelegateSessionPermissionProfileResolver",
            "WebViewPermissionKind.Other", "ResolveMenuPruningStage",
            "DrainSystemIntegrationEvents(", "PublishSystemIntegrationEvent(",
            "platform.source", "platform.pruningStage",
            "KeyDown +=", "KeyDown -=", "WebViewHostCapabilityCallOutcome",
            "Bridge.Expose<IThemeService>", "ThemeService", "AvaloniaThemeProvider"
        };
        foreach (var marker in appShellPresetMarkers)
            AssertSourceContains(appShellPreset, marker, TemplateMetadataSchema, appShellPresetPath);

        Assert.DoesNotContain("ExternalOpenHandler", appShellPreset, StringComparison.Ordinal);

        AssertSourceContains(desktopProject, "Agibuild.Fulora.Avalonia", TemplateMetadataSchema, desktopProjectPath);
        Assert.DoesNotContain(".WithInterFont()", desktopProgram, StringComparison.Ordinal);

        var indexMarkers = new[]
        {
            "DesktopHostService.ReadClipboardText", "DesktopHostService.WriteClipboardText",
            "DesktopHostService.ApplyMenuModel", "DesktopHostService.UpdateTrayState",
            "DesktopHostService.ExecuteSystemAction", "DesktopHostService.DrainSystemIntegrationEvents",
            "result.appliedTopLevelItems", "result.pruningStage", "readBoundedMetadata(",
            "platform.source", "platform.pruningStage", "source=", "profileVersion=",
            "platform.profileHash", "result.isVisible", "Host events", "System action denied",
            "window.runTemplateRegressionChecks", "setShowAboutScenario",
            "readSystemIntegrationStrategy", "mode=", "action=", "outcome=", "reason="
        };
        foreach (var marker in indexMarkers)
            AssertSourceContains(desktopIndex, marker, TemplateMetadataSchema, desktopIndexPath);

        var templateJsonPath = Path.Combine(repoRoot, "templates", "agibuild-hybrid", ".template.config", "template.json");
        var templateJson = File.ReadAllText(templateJsonPath);
        var reactTemplateWebPath = Path.Combine(repoRoot, "templates", "agibuild-hybrid", "HybridApp.Web.Vite.React");
        var vueTemplateWebPath = Path.Combine(repoRoot, "templates", "agibuild-hybrid", "HybridApp.Web.Vite.Vue");

        var templateJsonMarkers = new[]
        {
            "\"condition\": \"(shellPreset == 'baseline')\"",
            "\"exclude\": [\"HybridApp.Desktop/MainWindow.AppShellPreset.cs\"]",
            "\"condition\": \"(framework == 'react')\"",
            "\"condition\": \"(framework == 'vue')\"",
            "HybridApp.Web.Vite.React/**", "HybridApp.Web.Vite.Vue/**"
        };
        foreach (var marker in templateJsonMarkers)
            AssertSourceContains(templateJson, marker, TemplateMetadataSchema, templateJsonPath);

        Assert.True(Directory.Exists(reactTemplateWebPath), $"[{TemplateMetadataSchema}] Missing: {reactTemplateWebPath}");
        Assert.True(Directory.Exists(vueTemplateWebPath), $"[{TemplateMetadataSchema}] Missing: {vueTemplateWebPath}");
        AssertFileExists(Path.Combine(reactTemplateWebPath, "package.json"), TemplateMetadataSchema);
        AssertFileExists(Path.Combine(vueTemplateWebPath, "package.json"), TemplateMetadataSchema);
        Assert.DoesNotContain("DesktopHostService.DrainSystemIntegrationEvents", desktopMainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishSystemIntegrationEvent", desktopMainWindow, StringComparison.Ordinal);

        var reactPackageJson = File.ReadAllText(Path.Combine(reactTemplateWebPath, "package.json"));
        var vuePackageJson = File.ReadAllText(Path.Combine(vueTemplateWebPath, "package.json"));
        AssertSourceContains(reactPackageJson, "\"@agibuild/bridge\"", TemplateMetadataSchema, Path.Combine(reactTemplateWebPath, "package.json"));
        AssertSourceContains(vuePackageJson, "\"@agibuild/bridge\"", TemplateMetadataSchema, Path.Combine(vueTemplateWebPath, "package.json"));

        var reactServicesPath = Path.Combine(reactTemplateWebPath, "src", "bridge", "services.ts");
        var vueServicesPath = Path.Combine(vueTemplateWebPath, "src", "bridge", "services.ts");
        var reactHookPath = Path.Combine(reactTemplateWebPath, "src", "hooks", "useBridge.ts");
        var vueHookPath = Path.Combine(vueTemplateWebPath, "src", "composables", "useBridge.ts");
        AssertFileExists(reactServicesPath, TemplateMetadataSchema);
        AssertFileExists(vueServicesPath, TemplateMetadataSchema);
        AssertFileExists(reactHookPath, TemplateMetadataSchema);
        AssertFileExists(vueHookPath, TemplateMetadataSchema);

        var reactServices = File.ReadAllText(reactServicesPath);
        var vueServices = File.ReadAllText(vueServicesPath);
        var reactHook = File.ReadAllText(reactHookPath);
        var vueHook = File.ReadAllText(vueHookPath);

        Assert.True(
            reactServices.Contains("generated/bridge.client", StringComparison.Ordinal) ||
            reactServices.Contains("getService", StringComparison.Ordinal),
            $"[{TemplateMetadataSchema}] React template must expose service contracts via generated client or typed bridge service lookup.");
        Assert.True(
            vueServices.Contains("generated/bridge.client", StringComparison.Ordinal) ||
            vueServices.Contains("getService", StringComparison.Ordinal),
            $"[{TemplateMetadataSchema}] Vue template must expose service contracts via generated client or typed bridge service lookup.");

        Assert.True(
            reactHook.Contains("ready(", StringComparison.Ordinal) ||
            reactHook.Contains("bridge.ready(", StringComparison.Ordinal),
            $"[{TemplateMetadataSchema}] React template must use bridge readiness contract.");
        Assert.True(
            vueHook.Contains("ready(", StringComparison.Ordinal) ||
            vueHook.Contains("bridge.ready(", StringComparison.Ordinal),
            $"[{TemplateMetadataSchema}] Vue template must use bridge readiness contract.");

        var reactClientPath = Path.Combine(reactTemplateWebPath, "src", "bridge", "client.ts");
        if (File.Exists(reactClientPath))
        {
            var reactClient = File.ReadAllText(reactClientPath);
            Assert.True(
                reactClient.Contains("createBridgeProfile", StringComparison.Ordinal) ||
                reactClient.Contains("withLogging", StringComparison.Ordinal) ||
                reactClient.Contains("withErrorNormalization", StringComparison.Ordinal),
                $"[{TemplateMetadataSchema}] React bridge client should configure middleware through profile or explicit middleware wiring.");
        }
    }

    [Fact]
    public void Shell_production_matrix_declares_platform_coverage_and_executable_evidence()
    {
        var repoRoot = FindRepoRoot();
        var matrixPath = Path.Combine(repoRoot, "tests", "shell-production-matrix.json");
        var lanesPath = Path.Combine(repoRoot, "tests", "automation-lanes.json");

        using var matrixDoc = LoadJsonArtifact(matrixPath, ShellProductionMatrixSchema);
        using var lanesDoc = LoadJsonArtifact(lanesPath, ShellProductionMatrixSchema);

        var requiredPlatforms = new[] { "windows", "macos", "linux", "ios", "android" };
        var allowedCoverageTokens = new HashSet<string>(StringComparer.Ordinal) { "ct", "it-smoke", "it-soak", "n/a" };

        var laneNames = ExtractStringIds(lanesDoc.RootElement.GetProperty("lanes"), "name");
        var platforms = matrixDoc.RootElement.GetProperty("platforms").EnumerateArray()
            .Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        AssertContainsAll(platforms, requiredPlatforms, ShellProductionMatrixSchema, matrixPath);

        var capabilities = matrixDoc.RootElement.GetProperty("capabilities").EnumerateArray().ToList();
        Assert.NotEmpty(capabilities);

        var capabilityIds = capabilities.Select(x => x.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var requiredCapabilityIds = new[]
        {
            "shell-attach-detach-soak", "shell-multi-window-stress", "shell-host-capability-stress",
            "shell-product-experience-closure", "windows-webview2-teardown-stress",
            "shell-devtools-policy-isolation", "shell-devtools-lifecycle-cycles",
            "shell-shortcut-routing", "shell-system-integration-roundtrip",
            "shell-system-integration-v2-tray-payload", "shell-system-integration-v2-timestamp-normalization",
            "shell-system-integration-diagnostic-export"
        };
        AssertContainsAll(capabilityIds, requiredCapabilityIds, ShellProductionMatrixSchema, matrixPath);

        foreach (var capability in capabilities)
        {
            var capabilityId = capability.GetProperty("id").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(capability.GetProperty("supportLevel").GetString()));

            var coverage = capability.GetProperty("coverage");
            foreach (var platform in requiredPlatforms)
            {
                Assert.True(coverage.TryGetProperty(platform, out var coverageItems),
                    $"[{ShellProductionMatrixSchema}] Missing platform coverage '{platform}' in capability '{capabilityId}'.");

                var tokens = coverageItems.EnumerateArray().Select(x => x.GetString()!)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                Assert.NotEmpty(tokens);
                AssertControlledVocabulary(tokens, allowedCoverageTokens, ShellProductionMatrixSchema, $"capability '{capabilityId}' platform '{platform}'");

                if (platform is "ios" or "android")
                    Assert.All(tokens, token => Assert.Equal("n/a", token));
            }

            var evidenceItems = capability.GetProperty("evidence").EnumerateArray().ToList();
            Assert.NotEmpty(evidenceItems);

            foreach (var evidence in evidenceItems)
            {
                var lane = evidence.GetProperty("lane").GetString()!;
                Assert.Contains(lane, laneNames);
                AssertEvidenceLinkage(repoRoot, evidence.GetProperty("file").GetString()!,
                    evidence.GetProperty("testMethod").GetString()!, ShellProductionMatrixSchema);
            }
        }
    }

    [Fact]
    public void Host_capability_diagnostic_contract_and_external_open_path_remain_schema_stable()
    {
        var repoRoot = FindRepoRoot();
        var bridgePath = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Runtime", "Shell", "WebViewHostCapabilityBridge.cs");
        var shellPath = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Runtime", "Shell", "WebViewShellExperience.cs");
        var shellExecutorPath = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Runtime", "Shell", "WebViewHostCapabilityExecutor.cs");
        var shellNewWindowPath = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Runtime", "Shell", "WebViewNewWindowHandler.cs");
        var profilePath = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Runtime", "Shell", "WebViewSessionPermissionProfiles.cs");
        var helperPath = Path.Combine(repoRoot, "tests", "Agibuild.Fulora.Testing", "DiagnosticSchemaAssertionHelper.cs");
        var hostCapabilityUnitTestPath = Path.Combine(repoRoot, "tests", "Agibuild.Fulora.UnitTests", "HostCapabilityBridgeTests.cs");
        var hostCapabilityIntegrationTestPath = Path.Combine(repoRoot, "tests", "Agibuild.Fulora.Integration.Tests.Automation", "HostCapabilityBridgeIntegrationTests.cs");
        var profileIntegrationTestPath = Path.Combine(repoRoot, "tests", "Agibuild.Fulora.Integration.Tests.Automation", "MultiWindowLifecycleIntegrationTests.cs");

        foreach (var p in new[] { bridgePath, shellPath, shellExecutorPath, shellNewWindowPath, profilePath, helperPath, hostCapabilityUnitTestPath, hostCapabilityIntegrationTestPath, profileIntegrationTestPath })
            AssertFileExists(p, PhaseCloseoutConsistency);

        var bridgeSource = File.ReadAllText(bridgePath);
        var shellSource = string.Join(
            Environment.NewLine,
            File.ReadAllText(shellPath),
            File.ReadAllText(shellExecutorPath),
            File.ReadAllText(shellNewWindowPath));
        var profileSource = File.ReadAllText(profilePath);
        var helperSource = File.ReadAllText(helperPath);
        var hostCapabilityUnitTestSource = File.ReadAllText(hostCapabilityUnitTestPath);
        var hostCapabilityIntegrationTestSource = File.ReadAllText(hostCapabilityIntegrationTestPath);
        var profileIntegrationTestSource = File.ReadAllText(profileIntegrationTestPath);

        var bridgeSchemaMarkers = new[]
        {
            "public enum WebViewHostCapabilityCallOutcome",
            "Allow = 0", "Deny = 1", "Failure = 2",
            "MenuApplyModel = 6", "TrayUpdateState = 7", "SystemActionExecute = 8",
            "TrayInteractionEventDispatch = 9", "MenuInteractionEventDispatch = 10", "ShowAbout = 3",
            "public sealed class WebViewHostCapabilityBridgeOptions",
            "MinSystemIntegrationMetadataTotalLength = 256",
            "MaxSystemIntegrationMetadataTotalLength = 4096",
            "DefaultSystemIntegrationMetadataTotalLength = 1024",
            "SystemIntegrationMetadataAllowedPrefix = \"platform.\"",
            "SystemIntegrationMetadataExtensionPrefix = \"platform.extension.\"",
            "ReservedSystemIntegrationMetadataKeys",
            "system-integration-event-core-field-missing",
            "system-integration-event-metadata-namespace-invalid",
            "system-integration-event-metadata-key-unregistered",
            "system-integration-event-metadata-budget-exceeded",
            "public sealed class WebViewHostCapabilityDiagnosticEventArgs",
            $"CurrentDiagnosticSchemaVersion = {DiagnosticSchemaAssertionHelper.HostCapabilitySchemaVersion}",
            "public int DiagnosticSchemaVersion { get; }",
            "public Guid CorrelationId { get; }",
            "public WebViewHostCapabilityCallOutcome Outcome { get; }",
            "public WebViewOperationFailureCategory? FailureCategory { get; }",
            "CapabilityCallCompleted"
        };
        foreach (var marker in bridgeSchemaMarkers)
            AssertSourceContains(bridgeSource, marker, PhaseCloseoutConsistency, bridgePath);

        AssertSourceContains(helperSource, "public static class DiagnosticSchemaAssertionHelper", PhaseCloseoutConsistency, helperPath);
        AssertSourceContains(helperSource, "AssertHostCapabilityDiagnostic", PhaseCloseoutConsistency, helperPath);
        AssertSourceContains(helperSource, "AssertSessionProfileDiagnostic", PhaseCloseoutConsistency, helperPath);
        AssertSourceContains(hostCapabilityUnitTestSource, "DiagnosticSchemaAssertionHelper.AssertHostCapabilityDiagnostic", PhaseCloseoutConsistency, hostCapabilityUnitTestPath);
        AssertSourceContains(hostCapabilityIntegrationTestSource, "DiagnosticSchemaAssertionHelper.AssertHostCapabilityDiagnostic", PhaseCloseoutConsistency, hostCapabilityIntegrationTestPath);
        AssertSourceContains(profileIntegrationTestSource, "DiagnosticSchemaAssertionHelper.AssertSessionProfileDiagnostic", PhaseCloseoutConsistency, profileIntegrationTestPath);

        var shellMarkers = new[]
        {
            "SystemIntegration = 8",
            "SystemIntegrationEventReceived",
            "profile.ProfileVersion", "profile.ProfileHash"
        };
        foreach (var marker in shellMarkers)
            AssertSourceContains(shellSource, marker, PhaseCloseoutConsistency, $"{shellPath} (+collaborators)");

        AssertStringLiteralExists(
            shellSource,
            "Host capability bridge is required for ExternalBrowser strategy.",
            PhaseCloseoutConsistency,
            $"{shellPath} (+collaborators)");
        AssertMemberInvocationExists(shellSource, "HostCapabilityBridge", "ApplyMenuModel", PhaseCloseoutConsistency, $"{shellPath} (+collaborators)");
        AssertMemberInvocationExists(shellSource, "HostCapabilityBridge", "UpdateTrayState", PhaseCloseoutConsistency, $"{shellPath} (+collaborators)");
        AssertMemberInvocationExists(shellSource, "HostCapabilityBridge", "ExecuteSystemAction", PhaseCloseoutConsistency, $"{shellPath} (+collaborators)");
        AssertMemberInvocationExists(shellSource, "HostCapabilityBridge", "DispatchSystemIntegrationEvent", PhaseCloseoutConsistency, $"{shellPath} (+collaborators)");

        Assert.DoesNotContain("ExternalOpenHandler", shellSource, StringComparison.Ordinal);

        var profileMarkers = new[]
        {
            "public string? ProfileVersion { get; init; }", "public string? ProfileHash { get; init; }",
            "public string? ProfileVersion { get; }", "public string? ProfileHash { get; }",
            $"CurrentDiagnosticSchemaVersion = {DiagnosticSchemaAssertionHelper.SessionProfileSchemaVersion}",
            "public int DiagnosticSchemaVersion { get; }",
            "NormalizeProfileVersion", "NormalizeProfileHash"
        };
        foreach (var marker in profileMarkers)
            AssertSourceContains(profileSource, marker, PhaseCloseoutConsistency, profilePath);
    }

    [Fact]
    public void Ci_targets_enforce_openspec_strict_governance_gate()
    {
        var repoRoot = FindRepoRoot();
        var combinedSource = ReadCombinedBuildSource(repoRoot);
        var mainSource = File.ReadAllText(Path.Combine(repoRoot, "build", "Build.cs"));
        var dependencyGraph = ReadTargetDependencyGraph(combinedSource);
        var ciClosure = ReadTargetDependencyClosure(dependencyGraph, "Ci");

        var requiredTargets = new[]
        {
            "OpenSpecStrictGovernance", "DependencyVulnerabilityGovernance",
            "SampleTemplatePackageReferenceGovernance",
            "TypeScriptDeclarationGovernance", "ReleaseCloseoutSnapshot",
            "ContinuousTransitionGateGovernance", "DistributionReadinessGovernance",
            "AdoptionReadinessGovernance",
            "ReleaseOrchestrationGovernance"
        };
        foreach (var target in requiredTargets)
            AssertTargetDeclarationExists(combinedSource, target, CiTargetOpenSpecGate, "build/Build*.cs");

        AssertStringLiteralContains(combinedSource, "validate --all --strict", CiTargetOpenSpecGate, "build/Build*.cs");
        AssertInvocationExists(combinedSource, "RunProcessCheckedAsync", CiTargetOpenSpecGate, "build/Build*.cs");
        AssertStringLiteralExists(combinedSource, "dependency-governance-report.json", CiTargetOpenSpecGate, "build/Build*.cs");
        AssertStringLiteralExists(combinedSource, "typescript-governance-report.json", CiTargetOpenSpecGate, "build/Build*.cs");
        AssertStringLiteralExists(combinedSource, "sample-template-package-reference-governance-report.json", CiTargetOpenSpecGate, "build/Build*.cs");
        AssertStringLiteralExists(combinedSource, "closeout-snapshot.json", CiTargetOpenSpecGate, "build/Build*.cs");
        AssertStringLiteralExists(combinedSource, "transition-gate-governance-report.json", CiTargetOpenSpecGate, "build/Build*.cs");

        var ciDirectDependencies = new[]
        {
            "ReleaseOrchestrationGovernance", "SolutionConsistencyGovernance",
            "NugetPackageTest", "PackTemplate"
        };
        AssertTargetDependsOnContainsAll(mainSource, "Ci", ciDirectDependencies, CiTargetOpenSpecGate, "build/Build.cs");

        var ciRequiredClosureDependencies = new[]
        {
            "OpenSpecStrictGovernance", "DependencyVulnerabilityGovernance",
            "SampleTemplatePackageReferenceGovernance", "TypeScriptDeclarationGovernance",
            "ReleaseCloseoutSnapshot", "RuntimeCriticalPathExecutionGovernance",
            "ContinuousTransitionGateGovernance", "BridgeDistributionGovernance",
            "DistributionReadinessGovernance", "AdoptionReadinessGovernance",
            "ReleaseOrchestrationGovernance"
        };
        foreach (var dependency in ciRequiredClosureDependencies)
        {
            Assert.True(
                ciClosure.Contains(dependency),
                $"[{CiTargetOpenSpecGate}] Missing Ci transitive dependency '{dependency}'.");
        }

        var ciPublishDependencies = new[] { "Ci", "Publish" };
        AssertTargetDependsOnContainsAll(mainSource, "CiPublish", ciPublishDependencies, CiTargetOpenSpecGate, "build/Build.cs");
    }

    [Fact]
    public void Continuous_transition_gate_enforces_lane_parity_for_closeout_critical_groups()
    {
        var repoRoot = FindRepoRoot();
        var combinedSource = ReadCombinedBuildSource(repoRoot);
        var mainSource = File.ReadAllText(Path.Combine(repoRoot, "build", "Build.cs"));
        var dependencyGraph = ReadTargetDependencyGraph(combinedSource);
        var ciDependencyClosure = ReadTargetDependencyClosure(dependencyGraph, "Ci");
        var ciPublishDependsOn = ReadTargetDependsOnDependencies(mainSource, "CiPublish", TransitionGateParityConsistency, "build/Build.cs");

        var ciRequiredDependencies = new[]
        {
            "Coverage", "AutomationLaneReport", "WarningGovernance",
            "DependencyVulnerabilityGovernance", "TypeScriptDeclarationGovernance",
            "OpenSpecStrictGovernance", "ReleaseCloseoutSnapshot",
            "RuntimeCriticalPathExecutionGovernance", "AdoptionReadinessGovernance",
            "ContinuousTransitionGateGovernance"
        };

        foreach (var dep in ciRequiredDependencies)
        {
            Assert.True(
                ciDependencyClosure.Contains(dep),
                $"[{TransitionGateParityConsistency}] Missing Ci transitive dependency '{dep}'.");
        }

        Assert.True(
            ciPublishDependsOn.Contains("Ci"),
            $"[{TransitionGateParityConsistency}] CiPublish must depend on Ci.");
    }

    [Fact]
    public void Transition_gate_failures_use_governance_failure_schema()
    {
        const string artifactPath = "artifacts/test-results/transition-gate-governance-report.json";
        using var reportDoc = JsonDocument.Parse(
            """
            {
              "schemaVersion": 1,
              "failures": [
                {
                  "category": "release-closeout-snapshot",
                  "invariantId": "GOV-024",
                  "sourceArtifact": "build/Build*.cs",
                  "expected": "Ci: dependency closure contains ReleaseCloseoutSnapshot",
                  "actual": "Ci: missing"
                }
              ]
            }
            """);

        var failures = RequireReadinessFindingsArray(reportDoc.RootElement, "failures", TransitionGateDiagnosticSchema, artifactPath);
        var failure = Assert.Single(failures.EnumerateArray());
        AssertDistributionReadinessFailure(failure, TransitionGateDiagnosticSchema, artifactPath);

        using var invalidDiagnosticDoc = JsonDocument.Parse(
            """
            {
              "invariantId": "GOV-024",
              "sourceArtifact": "build/Build*.cs",
              "expected": "Ci: dependency closure contains Coverage",
              "actual": "Ci: missing"
            }
            """);

        Assert.Throws<GovernanceInvariantViolationException>(() =>
            AssertDistributionReadinessFailure(invalidDiagnosticDoc.RootElement, TransitionGateDiagnosticSchema, artifactPath));
    }

    [Fact]
    public void Phase_transition_roadmap_and_shell_governance_artifacts_remain_consistent()
    {
        var repoRoot = FindRepoRoot();
        var roadmapPath = Path.Combine(repoRoot, "openspec", "ROADMAP.md");
        var runtimeManifestPath = Path.Combine(repoRoot, "tests", "runtime-critical-path.manifest.json");
        var productionMatrixPath = Path.Combine(repoRoot, "tests", "shell-production-matrix.json");
        var templateIndexPath = Path.Combine(repoRoot, "templates", "agibuild-hybrid", "HybridApp.Desktop", "wwwroot", "index.html");
        var hostCapabilityBridgePath = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Runtime", "Shell", "WebViewHostCapabilityBridge.cs");

        foreach (var p in new[] { roadmapPath, runtimeManifestPath, productionMatrixPath, templateIndexPath, hostCapabilityBridgePath })
            AssertFileExists(p, PhaseTransitionConsistency);

        var roadmap = File.ReadAllText(roadmapPath);
        Assert.Matches(new Regex(@"## Phase \d+: .+\(✅ Completed\)", RegexOptions.Multiline), roadmap);
        AssertSourceContains(roadmap, "Completed phase id: `phase12-enterprise-advanced-scenarios`", PhaseTransitionConsistency, roadmapPath);
        AssertSourceContains(roadmap, "Active phase id: `post-roadmap-maintenance`", PhaseTransitionConsistency, roadmapPath);
        AssertSourceContains(roadmap, "Closeout snapshot artifact: `artifacts/test-results/closeout-snapshot.json`", PhaseTransitionConsistency, roadmapPath);
        AssertSourceContains(roadmap, "### Evidence Source Mapping", PhaseTransitionConsistency, roadmapPath);

        var completedPhaseCloseoutChangeIds = new[]
        {
            "2026-03-07-sentry-crash-reporting",
            "2026-03-07-shared-state-management",
            "2026-03-07-enterprise-auth-patterns",
            "2026-03-07-plugin-quality-compatibility"
        };
        foreach (var changeId in completedPhaseCloseoutChangeIds)
            AssertSourceContains(roadmap, changeId, PhaseTransitionConsistency, roadmapPath);

        Assert.Matches(new Regex(@"`nuke Test`: Unit `\d+`, Integration `\d+`, Total `\d+` \(pass\)", RegexOptions.Multiline), roadmap);
        Assert.Matches(new Regex(@"`nuke Coverage`: Line `\d+(\.\d+)?%` \(pass, threshold `\d+%`\)", RegexOptions.Multiline), roadmap);

        using var runtimeDoc = LoadJsonArtifact(runtimeManifestPath, PhaseTransitionConsistency);
        using var matrixDoc = LoadJsonArtifact(productionMatrixPath, PhaseTransitionConsistency);

        var runtimeScenarioIds = ExtractStringIds(runtimeDoc.RootElement.GetProperty("scenarios"), "id");
        var matrixCapabilityIds = ExtractStringIds(matrixDoc.RootElement.GetProperty("capabilities"), "id");

        var sharedTransitionCapabilityIds = new[]
        {
            "shell-system-integration-roundtrip", "shell-system-integration-v2-tray-payload",
            "shell-system-integration-v2-timestamp-normalization", "shell-system-integration-diagnostic-export"
        };
        AssertContainsAll(runtimeScenarioIds, sharedTransitionCapabilityIds, PhaseTransitionConsistency, runtimeManifestPath);
        AssertContainsAll(matrixCapabilityIds, sharedTransitionCapabilityIds, PhaseTransitionConsistency, productionMatrixPath);

        AssertSourceContains(File.ReadAllText(templateIndexPath), "window.runTemplateRegressionChecks", PhaseTransitionConsistency, templateIndexPath);
        var bridgeSource = File.ReadAllText(hostCapabilityBridgePath);
        AssertSourceContains(bridgeSource, "ToExportRecord", PhaseTransitionConsistency, hostCapabilityBridgePath);
        AssertSourceContains(bridgeSource, "WebViewHostCapabilityDiagnosticExportRecord", PhaseTransitionConsistency, hostCapabilityBridgePath);
    }

    [Fact]
    public void Warning_governance_treats_windowsbase_conflicts_as_regressions()
    {
        var repoRoot = FindRepoRoot();
        var warningGovernancePath = Path.Combine(repoRoot, "build", "Build.WarningGovernance.cs");
        AssertFileExists(warningGovernancePath, WindowsBaseConflictGovernance);

        var source = File.ReadAllText(warningGovernancePath);
        AssertSourceContains(source, "WindowsBase conflict warning must be eliminated; baseline acceptance is not allowed.", WindowsBaseConflictGovernance, warningGovernancePath);
        Assert.DoesNotContain("WindowsBase conflict is governed by approved baseline metadata.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_metadata_contains_required_properties_for_stable_release()
    {
        var repoRoot = FindRepoRoot();
        var directoryBuildPropsPath = Path.Combine(repoRoot, "Directory.Build.props");
        AssertFileExists(directoryBuildPropsPath, PackageMetadata);

        var props = File.ReadAllText(directoryBuildPropsPath);
        AssertSourceContains(props, "<PackageLicenseExpression>", PackageMetadata, directoryBuildPropsPath);
        AssertSourceContains(props, "<PackageProjectUrl>", PackageMetadata, directoryBuildPropsPath);
        AssertSourceContains(props, "<VersionPrefix>", PackageMetadata, directoryBuildPropsPath);
        Assert.Matches(@"<VersionPrefix>\d+\.\d+\.\d+</VersionPrefix>", props);

        var mainCsprojPath = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Avalonia", "Agibuild.Fulora.Avalonia.csproj");
        AssertFileExists(mainCsprojPath, PackageMetadata);

        var csproj = File.ReadAllText(mainCsprojPath);
        AssertSourceContains(csproj, "<Description>", PackageMetadata, mainCsprojPath);
        Assert.DoesNotContain("preview", csproj.ToLowerInvariant().Split("<description>").Last().Split("</description>").First());
    }

    [Fact]
    public void Unified_ci_workflow_enforces_manual_release_approval_and_single_release_entrypoint()
    {
        var repoRoot = FindRepoRoot();
        var ciWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
        var releaseWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");
        var createTagWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "create-tag.yml");

        AssertFileExists(ciWorkflowPath, ReleaseOrchestrationDecisionGate);
        Assert.False(File.Exists(releaseWorkflowPath),
            $"[{ReleaseOrchestrationDecisionGate}] release.yml should be removed after unified workflow cutover.");
        Assert.False(File.Exists(createTagWorkflowPath),
            $"[{ReleaseOrchestrationDecisionGate}] create-tag.yml should be removed; tag creation is in unified workflow release stage.");

        var ciWorkflow = File.ReadAllText(ciWorkflowPath);

        AssertSourceContains(ciWorkflow, "name: CI and Release", ReleaseOrchestrationDecisionGate, ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "needs: [version, build-macos, build-windows, build-linux, build-docs]", ReleaseOrchestrationDecisionGate, ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "environment: release", ReleaseOrchestrationDecisionGate, ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "if: needs.version.outputs.is_release == 'true'", ReleaseOrchestrationDecisionGate, ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "Create and push tag", ReleaseOrchestrationDecisionGate, ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "Create GitHub Release", ReleaseOrchestrationDecisionGate, ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "gh release create", ReleaseOrchestrationDecisionGate, ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "deploy-docs", ReleaseOrchestrationDecisionGate, ciWorkflowPath);
    }

    [Fact]
    public void Readme_quality_signals_match_actual_test_evidence()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        AssertFileExists(readmePath, ReadmeQualitySignals);

        var readme = File.ReadAllText(readmePath);

        Assert.True(
            Directory.GetFiles(Path.Combine(repoRoot, "tests", "Agibuild.Fulora.UnitTests"), "*Tests.cs", SearchOption.AllDirectories).Length > 0,
            $"[{ReadmeQualitySignals}] No unit test files found");
        Assert.True(
            Directory.GetFiles(Path.Combine(repoRoot, "tests", "Agibuild.Fulora.Integration.Tests.Automation"), "*Tests.cs", SearchOption.AllDirectories).Length > 0,
            $"[{ReadmeQualitySignals}] No integration test files found");

        Assert.Matches(new Regex(@"img\.shields\.io/endpoint\?url=.*unit-tests\.json"), readme);
        Assert.Matches(new Regex(@"img\.shields\.io/endpoint\?url=.*integration-tests\.json"), readme);
        Assert.Matches(new Regex(@"img\.shields\.io/endpoint\?url=.*line-coverage\.json"), readme);
        Assert.Matches(new Regex(@"img\.shields\.io/endpoint\?url=.*branch-coverage\.json"), readme);
        AssertSourceContains(readme, "Phase 12", ReadmeQualitySignals, readmePath);
    }

    [Fact]
    public void Coverage_target_enforces_branch_coverage_threshold()
    {
        var repoRoot = FindRepoRoot();
        var buildSource = ReadCombinedBuildSource(repoRoot);

        var markers = new[] { "BranchCoverageThreshold", "branch-rate", "Branch coverage", "branchThreshold", "dependencyGovernanceReportExists" };
        foreach (var marker in markers)
            AssertSourceContains(buildSource, marker, CoverageThreshold, "build/Build*.cs");
    }

    [Fact]
    public void Shell_matrix_and_runtime_manifest_are_kept_in_sync_for_shell_capabilities()
    {
        var repoRoot = FindRepoRoot();
        var runtimeManifestPath = Path.Combine(repoRoot, "tests", "runtime-critical-path.manifest.json");
        var matrixPath = Path.Combine(repoRoot, "tests", "shell-production-matrix.json");

        using var runtimeDoc = LoadJsonArtifact(runtimeManifestPath, ShellManifestMatrixSync);
        using var matrixDoc = LoadJsonArtifact(matrixPath, ShellManifestMatrixSync);

        var runtimeShellIds = ExtractStringIds(runtimeDoc.RootElement.GetProperty("scenarios"), "id");
        var matrixCapabilityIds = ExtractStringIds(matrixDoc.RootElement.GetProperty("capabilities"), "id");

        AssertBidirectionalSync(
            runtimeShellIds, "runtime-critical-path",
            matrixCapabilityIds, "shell-production-matrix",
            ShellManifestMatrixSync,
            id => id.StartsWith("shell-", StringComparison.Ordinal));
    }

    [Fact]
    public void Benchmark_baseline_artifact_has_required_metrics_and_tolerance()
    {
        var repoRoot = FindRepoRoot();
        var baselinePath = Path.Combine(repoRoot, "tests", "performance-benchmark-baseline.json");

        using var doc = LoadJsonArtifact(baselinePath, BenchmarkBaselineSchema);
        RequireVersionField(doc.RootElement, BenchmarkBaselineSchema, baselinePath, minimumVersion: 1);

        var toleranceElement = RequireProperty(doc.RootElement, "allowedRegressionPercent", BenchmarkBaselineSchema, baselinePath);
        Assert.True(toleranceElement.GetDouble() > 0, $"[{BenchmarkBaselineSchema}] allowedRegressionPercent must be > 0.");

        var metrics = RequireProperty(doc.RootElement, "metrics", BenchmarkBaselineSchema, baselinePath);
        var metricsList = metrics.EnumerateArray().ToList();
        Assert.NotEmpty(metricsList);

        foreach (var metric in metricsList)
        {
            var id = metric.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id), $"[{BenchmarkBaselineSchema}] Metric id must not be empty.");
            Assert.True(metric.GetProperty("baselineMs").GetDouble() > 0, $"[{BenchmarkBaselineSchema}] Metric '{id}' baselineMs must be > 0.");
        }
    }

    [Fact]
    public void Dx_assets_for_bridge_package_and_official_samples_use_profile_and_generated_contracts()
    {
        var repoRoot = FindRepoRoot();

        var requiredFiles = new[]
        {
            "packages/bridge/package.json", "packages/bridge/src/index.ts",
            "packages/bridge/src/profile.ts",
            "templates/agibuild-hybrid/HybridApp.Desktop/MainWindow.axaml.cs",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.React/src/bridge/client.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.React/src/bridge/services.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.React/src/hooks/useBridge.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.Vue/src/bridge/client.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.Vue/src/bridge/services.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.Vue/src/composables/useBridge.ts",
            "samples/avalonia-react/AvaloniReact.Web/package.json",
            "samples/avalonia-react/AvaloniReact.Web/src/bridge/services.ts",
            "samples/avalonia-react/AvaloniReact.Web/src/hooks/useBridge.ts",
            "samples/avalonia-vue/AvaloniVue.Web/src/main.ts",
            "samples/avalonia-vue/AvaloniVue.Web/package.json",
            "samples/avalonia-vue/AvaloniVue.Web/src/bridge/services.ts",
            "samples/avalonia-vue/AvaloniVue.Web/src/bridge/client.ts",
            "samples/avalonia-vue/AvaloniVue.Web/tsconfig.json",
            "samples/showcase-todo/ShowcaseTodo.Web/src/bridge/services.ts",
            "samples/showcase-todo/ShowcaseTodo.Web/src/bridge/client.ts",
            "samples/showcase-todo/ShowcaseTodo.Web/src/hooks/useBridge.ts",
            "samples/avalonia-ai-chat/AvaloniAiChat.Web/src/bridge/services.ts",
            "samples/avalonia-ai-chat/AvaloniAiChat.Web/src/bridge/client.ts",
            "samples/avalonia-ai-chat/AvaloniAiChat.Web/src/hooks/useBridge.ts",
            "samples/avalonia-ai-chat/AvaloniAiChat.Web/src/App.tsx"
        };
        foreach (var relPath in requiredFiles)
            AssertFileExists(Path.Combine(repoRoot, relPath.Replace('/', Path.DirectorySeparatorChar)), BridgeDxAssets);

        var bridgePackage = File.ReadAllText(Path.Combine(repoRoot, "packages", "bridge", "package.json"));
        var bridgeEntry = File.ReadAllText(Path.Combine(repoRoot, "packages", "bridge", "src", "index.ts"));
        var bridgeProfileEntry = File.ReadAllText(Path.Combine(repoRoot, "packages", "bridge", "src", "profile.ts"));
        var templateDesktopMainWindow = File.ReadAllText(Path.Combine(repoRoot, "templates", "agibuild-hybrid", "HybridApp.Desktop", "MainWindow.axaml.cs"));
        var templateReactClient = File.ReadAllText(Path.Combine(repoRoot, "templates", "agibuild-hybrid", "HybridApp.Web.Vite.React", "src", "bridge", "client.ts"));
        var templateVueClient = File.ReadAllText(Path.Combine(repoRoot, "templates", "agibuild-hybrid", "HybridApp.Web.Vite.Vue", "src", "bridge", "client.ts"));
        var reactPackage = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-react", "AvaloniReact.Web", "package.json"));
        var reactBridge = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-react", "AvaloniReact.Web", "src", "bridge", "services.ts"));
        var reactBridgeHook = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-react", "AvaloniReact.Web", "src", "hooks", "useBridge.ts"));
        var vuePackage = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-vue", "AvaloniVue.Web", "package.json"));
        var vueBridge = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-vue", "AvaloniVue.Web", "src", "bridge", "services.ts"));
        var vueClient = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-vue", "AvaloniVue.Web", "src", "bridge", "client.ts"));
        var vueTsConfig = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-vue", "AvaloniVue.Web", "tsconfig.json"));
        var todoBridge = File.ReadAllText(Path.Combine(repoRoot, "samples", "showcase-todo", "ShowcaseTodo.Web", "src", "bridge", "services.ts"));
        var todoClient = File.ReadAllText(Path.Combine(repoRoot, "samples", "showcase-todo", "ShowcaseTodo.Web", "src", "bridge", "client.ts"));
        var aiChatBridge = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-ai-chat", "AvaloniAiChat.Web", "src", "bridge", "services.ts"));
        var aiChatClient = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-ai-chat", "AvaloniAiChat.Web", "src", "bridge", "client.ts"));
        var aiChatApp = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-ai-chat", "AvaloniAiChat.Web", "src", "App.tsx"));
        var vueLayout = File.ReadAllText(Path.Combine(repoRoot, "samples", "avalonia-vue", "AvaloniVue.Web", "src", "components", "AppLayout.vue"));

        AssertSourceContains(bridgePackage, "\"@agibuild/bridge\"", BridgeDxAssets, "packages/bridge/package.json");
        AssertSourceContains(bridgePackage, "\"prepare\": \"npm run build\"", BridgeDxAssets, "packages/bridge/package.json");
        AssertSourceContains(bridgeEntry, "createBridgeClient", BridgeDxAssets, "packages/bridge/src/index.ts");
        AssertSourceContains(bridgeEntry, "bridgeClient", BridgeDxAssets, "packages/bridge/src/index.ts");
        AssertSourceContains(bridgeEntry, "getService", BridgeDxAssets, "packages/bridge/src/index.ts");
        AssertSourceContains(bridgeProfileEntry, "createBridgeProfile", BridgeDxAssets, "packages/bridge/src/profile.ts");
        AssertSourceContains(bridgeProfileEntry, "withErrorNormalization", BridgeDxAssets, "packages/bridge/src/profile.ts");
        AssertSourceContains(templateDesktopMainWindow, "BootstrapSpaProfileAsync", BridgeDxAssets, "templates/agibuild-hybrid/HybridApp.Desktop/MainWindow.axaml.cs");
        AssertSourceContains(templateReactClient, "@agibuild/bridge/profile", BridgeDxAssets, "templates/agibuild-hybrid/HybridApp.Web.Vite.React/src/bridge/client.ts");
        AssertSourceContains(templateVueClient, "@agibuild/bridge/profile", BridgeDxAssets, "templates/agibuild-hybrid/HybridApp.Web.Vite.Vue/src/bridge/client.ts");
        AssertSourceContains(reactPackage, "\"@agibuild/bridge\"", BridgeDxAssets, "samples/avalonia-react/.../package.json");
        AssertSourceContains(reactBridge, "from './generated/bridge.client'", BridgeDxAssets, "samples/avalonia-react/.../services.ts");
        AssertSourceContains(reactBridgeHook, "bridgeProfile.ready", BridgeDxAssets, "samples/avalonia-react/.../useBridge.ts");
        AssertSourceContains(vueLayout, "getAppInfo", BridgeDxAssets, "samples/avalonia-vue/.../AppLayout.vue");
        AssertSourceContains(vuePackage, "\"@agibuild/bridge\"", BridgeDxAssets, "samples/avalonia-vue/.../package.json");
        AssertSourceContains(vueBridge, "from './generated/bridge.client'", BridgeDxAssets, "samples/avalonia-vue/.../services.ts");
        AssertSourceContains(vueClient, "@agibuild/bridge/profile", BridgeDxAssets, "samples/avalonia-vue/.../client.ts");
        AssertSourceContains(vueTsConfig, "src/bridge/generated/bridge.d.ts", BridgeDxAssets, "samples/avalonia-vue/.../tsconfig.json");
        AssertSourceContains(todoBridge, "from './generated/bridge.client'", BridgeDxAssets, "samples/showcase-todo/.../services.ts");
        AssertSourceContains(todoClient, "@agibuild/bridge/profile", BridgeDxAssets, "samples/showcase-todo/.../client.ts");
        AssertSourceContains(aiChatBridge, "from './generated/bridge.client'", BridgeDxAssets, "samples/avalonia-ai-chat/.../services.ts");
        AssertSourceContains(aiChatClient, "@agibuild/bridge/profile", BridgeDxAssets, "samples/avalonia-ai-chat/.../client.ts");
        AssertSourceContains(aiChatApp, "from './bridge/services'", BridgeDxAssets, "samples/avalonia-ai-chat/.../App.tsx");
    }

    [Fact]
    public void Official_sample_and_template_app_layer_paths_enforce_single_entry_bridge_policy_without_exceptions()
    {
        var repoRoot = FindRepoRoot();
        const string strictScope = "official-maintained-app-layer::strict-no-exception";

        var governedFiles = new[]
        {
            "templates/agibuild-hybrid/HybridApp.Web.Vite.React/src/bridge/client.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.React/src/hooks/useBridge.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.React/src/bridge/services.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.Vue/src/bridge/client.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.Vue/src/composables/useBridge.ts",
            "templates/agibuild-hybrid/HybridApp.Web.Vite.Vue/src/bridge/services.ts",
            "samples/avalonia-react/AvaloniReact.Web/src/bridge/client.ts",
            "samples/avalonia-react/AvaloniReact.Web/src/hooks/useBridge.ts",
            "samples/avalonia-react/AvaloniReact.Web/src/bridge/services.ts",
            "samples/avalonia-vue/AvaloniVue.Web/src/bridge/client.ts",
            "samples/avalonia-vue/AvaloniVue.Web/src/composables/useBridge.ts",
            "samples/avalonia-vue/AvaloniVue.Web/src/bridge/services.ts",
            "samples/showcase-todo/ShowcaseTodo.Web/src/bridge/client.ts",
            "samples/showcase-todo/ShowcaseTodo.Web/src/hooks/useBridge.ts",
            "samples/showcase-todo/ShowcaseTodo.Web/src/bridge/services.ts",
            "samples/avalonia-ai-chat/AvaloniAiChat.Web/src/bridge/client.ts",
            "samples/avalonia-ai-chat/AvaloniAiChat.Web/src/hooks/useBridge.ts",
            "samples/avalonia-ai-chat/AvaloniAiChat.Web/src/bridge/services.ts",
            "samples/avalonia-ai-chat/AvaloniAiChat.Web/src/App.tsx",
            "templates/agibuild-hybrid/HybridApp.Desktop/MainWindow.axaml.cs",
            "samples/avalonia-react/AvaloniReact.Desktop/MainWindow.axaml.cs",
            "samples/avalonia-vue/AvaloniVue.Desktop/MainWindow.axaml.cs",
            "samples/showcase-todo/ShowcaseTodo.Desktop/MainWindow.axaml.cs",
            "samples/avalonia-ai-chat/AvaloniAiChat.Desktop/MainWindow.axaml.cs"
        };

        var prohibitedMarkers = new[]
        {
            "window.agWebView",
            ".rpc.invoke(",
            "bridgeClient.getService",
            "createBridgeClient(",
            "EnableSpaHosting(",
            "BootstrapSpaAsync(",
            "WebView.NavigateAsync("
        };

        foreach (var relPath in governedFiles)
        {
            var path = Path.Combine(repoRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
            AssertFileExists(path, BridgeSingleEntryAppLayerPolicy);
            var source = File.ReadAllText(path);

            foreach (var marker in prohibitedMarkers)
            {
                if (!source.Contains(marker, StringComparison.Ordinal))
                    continue;

                throw new GovernanceInvariantViolationException(
                    BridgeSingleEntryAppLayerPolicy,
                    relPath,
                    $"marker absent '{marker}' (scope={strictScope}, decision=deny)",
                    $"marker present '{marker}' (scope={strictScope}, decision=deny)");
            }
        }
    }

    [Fact]
    public void TypeScript_governance_target_emits_single_entry_semantic_diagnostics_and_blocks_violations()
    {
        var repoRoot = FindRepoRoot();
        var governanceSourcePath = Path.Combine(repoRoot, "build", "Build.Governance.TypeScript.cs");
        AssertFileExists(governanceSourcePath, BridgeSingleEntryAppLayerPolicy);

        var source = File.ReadAllText(governanceSourcePath);
        AssertSourceContains(source, "BridgeSingleEntryAppLayerPolicyInvariantId", BridgeSingleEntryAppLayerPolicy, governanceSourcePath);
        AssertSourceContains(source, "semanticDiagnostics", BridgeSingleEntryAppLayerPolicy, governanceSourcePath);
        AssertSourceContains(source, "official-maintained-app-layer::strict-no-exception", BridgeSingleEntryAppLayerPolicy, governanceSourcePath);
        AssertSourceContains(source, "createBridgeProfile", BridgeSingleEntryAppLayerPolicy, governanceSourcePath);
        AssertSourceContains(source, "BootstrapSpaProfileAsync", BridgeSingleEntryAppLayerPolicy, governanceSourcePath);
        AssertSourceContains(source, "marker present", BridgeSingleEntryAppLayerPolicy, governanceSourcePath);
    }

    [Fact]
    public void Sample_and_template_projects_use_package_references_without_source_or_conditional_project_references()
    {
        var repoRoot = FindRepoRoot();
        var governedRoots = new[]
        {
            Path.Combine(repoRoot, "samples"),
            Path.Combine(repoRoot, "templates")
        };
        var projectFiles = governedRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(projectFiles);

        var sourceProjectReferencePattern = new Regex(
            @"<(ProjectReference|Import)\b[^>]*(Include|Project)\s*=\s*""(?<path>[^""]*src[\\/]+Agibuild\.Fulora[^""]*)""[^>]*>",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var conditionalProjectReferencePattern = new Regex(
            @"<ProjectReference\b[^>]*\bCondition\s*=",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var packageReferenceWithVersionAttributePattern = new Regex(
            @"<PackageReference\b[^>]*Include=""(?<id>Agibuild\.Fulora(?:\.[^""]+)?)""[^>]*Version=""(?<version>[^""]+)""[^>]*/?>",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var packageReferenceElementPattern = new Regex(
            @"<PackageReference\b[^>]*Include=""(?<id>Agibuild\.Fulora(?:\.[^""]+)?)""[^>]*>(?<body>[\s\S]*?)</PackageReference>",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var versionElementPattern = new Regex(
            @"<Version>\s*(?<version>[^<]+)\s*</Version>",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        foreach (var projectFile in projectFiles)
        {
            var relativePath = Path.GetRelativePath(repoRoot, projectFile).Replace(Path.DirectorySeparatorChar, '/');
            var source = File.ReadAllText(projectFile);

            Assert.False(
                sourceProjectReferencePattern.IsMatch(source),
                $"[{SampleTemplatePackageReferencePolicy}] {relativePath} must not reference src/Agibuild.Fulora.* via ProjectReference/Import.");
            Assert.False(
                conditionalProjectReferencePattern.IsMatch(source),
                $"[{SampleTemplatePackageReferencePolicy}] {relativePath} must not contain conditional ProjectReference.");

            foreach (Match match in packageReferenceWithVersionAttributePattern.Matches(source))
            {
                var packageId = match.Groups["id"].Value;
                var version = match.Groups["version"].Value.Trim();
                Assert.True(
                    string.Equals(version, "*-*", StringComparison.Ordinal),
                    $"[{SampleTemplatePackageReferencePolicy}] {relativePath} package '{packageId}' must use '*-*', actual '{version}'.");
            }

            foreach (Match match in packageReferenceElementPattern.Matches(source))
            {
                var packageId = match.Groups["id"].Value;
                var body = match.Groups["body"].Value;
                var versionMatch = versionElementPattern.Match(body);
                if (!versionMatch.Success)
                    continue;

                var version = versionMatch.Groups["version"].Value.Trim();
                Assert.True(
                    string.Equals(version, "*-*", StringComparison.Ordinal),
                    $"[{SampleTemplatePackageReferencePolicy}] {relativePath} package '{packageId}' must use '*-*', actual '{version}'.");
            }
        }
    }

    [Fact]
    public void Ci_evidence_snapshot_build_target_emits_v2_schema_with_provenance()
    {
        var repoRoot = FindRepoRoot();
        var combinedSource = ReadCombinedBuildSource(repoRoot);

        AssertAssignmentValueIn(
            combinedSource,
            "schemaVersion",
            ["2"],
            EvidenceContractV2Schema,
            "build/Build.Governance.Release.cs");
        AssertAssignmentValueIn(
            combinedSource,
            "laneContext",
            ["\"Ci\"", "LaneContextCi"],
            EvidenceContractV2Schema,
            "build/Build.Governance.Release.cs");
        AssertAssignmentValueIn(
            combinedSource,
            "producerTarget",
            ["\"ReleaseCloseoutSnapshot\""],
            EvidenceContractV2Schema,
            "build/Build.Governance.Release.cs");
        AssertAnonymousObjectMemberAssignedWithNew(combinedSource, "transition", EvidenceContractV2Schema, "build/Build.Governance.Release.cs");
        AssertAnonymousObjectMemberAssignedWithNew(combinedSource, "transitionContinuity", EvidenceContractV2Schema, "build/Build.Governance.Release.cs");
        AssertIndexerStringKeyAssignmentExists(combinedSource, "releaseDecision", EvidenceContractV2Schema, "build/Build.Governance.Release.cs");
        AssertIndexerStringKeyAssignmentExists(combinedSource, "releaseBlockingReasons", EvidenceContractV2Schema, "build/Build.Governance.Release.cs");
        AssertAnonymousObjectHasMembers(
            combinedSource,
            ["completedPhase", "activePhase", "closeoutArchives", "distributionReadiness", "adoptionReadiness"],
            EvidenceContractV2Schema,
            "build/Build.Governance.Release.cs");
        AssertSourceContains(combinedSource, "TransitionLaneProvenanceInvariantId", EvidenceContractV2Schema, "build/Build.Governance.Release.cs");
        AssertSourceContains(combinedSource, "closeout-snapshot.json", EvidenceContractV2Schema, "build/Build.cs");
        AssertSourceContains(combinedSource, "distribution-readiness-governance-report.json", EvidenceContractV2Schema, "build/Build.cs");
        AssertSourceContains(combinedSource, "adoption-readiness-governance-report.json", EvidenceContractV2Schema, "build/Build.cs");
        AssertSourceContains(combinedSource, "release-orchestration-decision-report.json", EvidenceContractV2Schema, "build/Build.cs");
    }

    [Fact]
    public void Bridge_distribution_governance_target_exists_in_cipublish_with_v2_provenance()
    {
        var repoRoot = FindRepoRoot();
        var combinedSource = ReadCombinedBuildSource(repoRoot);
        var dependencyGraph = ReadTargetDependencyGraph(combinedSource);
        var ciDependencyClosure = ReadTargetDependencyClosure(dependencyGraph, "Ci");

        AssertTargetDeclarationExists(combinedSource, "BridgeDistributionGovernance", BridgeDistributionParity, "build/Build.Governance.Distribution.cs");
        AssertStringLiteralExists(combinedSource, "bridge-distribution-governance-report.json", BridgeDistributionParity, "build/Build*.cs");
        AssertAssignmentValueIn(
            combinedSource,
            "producerTarget",
            ["\"BridgeDistributionGovernance\""],
            BridgeDistributionParity,
            "build/Build.Governance.Distribution.cs");
        AssertStringLiteralExists(combinedSource, "SMOKE_PASSED", BridgeDistributionParity, "build/Build.Governance.Distribution.cs");
        AssertStringLiteralExists(combinedSource, "LTS_IMPORT_OK", BridgeDistributionParity, "build/Build.Governance.Distribution.cs");
        AssertInvocationExists(combinedSource, "IsToolAvailableAsync", BridgeDistributionParity, "build/Build*.cs");
        AssertInvocationFirstArgumentIn(
            combinedSource,
            "RunProcessCheckedAsync",
            new HashSet<string>(StringComparer.Ordinal) { "toolName" },
            BridgeDistributionParity,
            "build/Build*.cs");
        AssertSourceContains(combinedSource, "{toolName} --version", BridgeDistributionParity, "build/Build*.cs");

        Assert.True(
            ciDependencyClosure.Contains("BridgeDistributionGovernance"),
            $"[{BridgeDistributionParity}] Missing Ci transitive dependency 'BridgeDistributionGovernance'.");
    }

    [Fact]
    public void CiPublish_release_orchestration_gate_runs_before_publish_side_effects()
    {
        var repoRoot = FindRepoRoot();
        var mainSource = File.ReadAllText(Path.Combine(repoRoot, "build", "Build.cs"));
        var governanceSource = File.ReadAllText(Path.Combine(repoRoot, "build", "Build.Governance.Release.cs"));

        AssertTargetDependsOnContainsAll(
            mainSource,
            "Ci",
            ["ReleaseOrchestrationGovernance"],
            ReleaseOrchestrationDecisionGate,
            "build/Build.cs");
        AssertTargetDependsOnContainsAll(
            mainSource,
            "CiPublish",
            ["Ci", "Publish"],
            ReleaseOrchestrationDecisionGate,
            "build/Build.cs");

        AssertTargetDeclarationExists(governanceSource, "ReleaseOrchestrationGovernance", ReleaseOrchestrationDecisionGate, "build/Build.Governance.Release.cs");
        AssertTargetDependsOnContainsAll(
            governanceSource,
            "ReleaseOrchestrationGovernance",
            ["ContinuousTransitionGateGovernance", "ValidatePackage"],
            ReleaseOrchestrationDecisionGate,
            "build/Build.Governance.Release.cs");
        AssertSourceContains(governanceSource, "Release orchestration governance blocked publication", ReleaseOrchestrationDecisionGate, "build/Build.Governance.Release.cs");
    }

    [Fact]
    public void Ci_evidence_v2_release_decision_requires_structured_blocking_reason_schema()
    {
        const string artifactPath = "artifacts/test-results/closeout-snapshot.json";
        using var validDoc = JsonDocument.Parse(
            """
            {
              "schemaVersion": 2,
              "releaseDecision": {
                "state": "blocked",
                "isStableRelease": true
              },
              "releaseBlockingReasons": [
                {
                  "category": "governance",
                  "invariantId": "GOV-027",
                  "sourceArtifact": "artifacts/test-results/transition-gate-governance-report.json",
                  "expected": "failureCount = 0",
                  "actual": "failureCount = 1"
                }
              ]
            }
            """);

        var decision = RequireReleaseDecision(validDoc.RootElement, ReleaseOrchestrationDecisionGate, artifactPath);
        Assert.Equal("blocked", decision.GetProperty("state").GetString());

        var reasons = RequireReleaseBlockingReasons(validDoc.RootElement, ReleaseOrchestrationReasonSchema, artifactPath);
        Assert.Single(reasons.EnumerateArray());
        var reason = reasons.EnumerateArray().First();
        AssertReleaseBlockingReason(reason, ReleaseOrchestrationReasonSchema, artifactPath);
        AssertControlledVocabulary(
            [reason.GetProperty("category").GetString()!],
            new HashSet<string>(StringComparer.Ordinal) { "evidence", "package-metadata", "governance", "quality-threshold" },
            ReleaseOrchestrationReasonSchema,
            "releaseBlockingReasons.category");

        using var invalidDoc = JsonDocument.Parse(
            """
            {
              "releaseDecision": {
                "state": "blocked"
              },
              "releaseBlockingReasons": [
                {
                  "invariantId": "GOV-027",
                  "sourceArtifact": "artifacts/test-results/closeout-snapshot.json",
                  "expected": "artifact exists",
                  "actual": "missing"
                }
              ]
            }
            """);

        var invalidReasons = RequireReleaseBlockingReasons(invalidDoc.RootElement, ReleaseOrchestrationReasonSchema, artifactPath);
        Assert.Throws<GovernanceInvariantViolationException>(() =>
            AssertReleaseBlockingReason(invalidReasons.EnumerateArray().First(), ReleaseOrchestrationReasonSchema, artifactPath));
    }

    [Fact]
    public void Stable_publish_requires_release_orchestration_ready_state()
    {
        var repoRoot = FindRepoRoot();
        var governanceSource = File.ReadAllText(Path.Combine(repoRoot, "build", "Build.Governance.Release.cs"));
        var mainSource = File.ReadAllText(Path.Combine(repoRoot, "build", "Build.cs"));

        AssertInvocationFirstArgumentIn(
            governanceSource,
            "ResolvePackedAgibuildVersion",
            new HashSet<string>(StringComparer.Ordinal) { "\"Agibuild.Fulora.Avalonia\"", "PrimaryHostPackageId" },
            StablePublishReadiness,
            "build/Build.Governance.Release.cs");
        AssertSourceContains(governanceSource, "isStableRelease", StablePublishReadiness, "build/Build.Governance.Release.cs");
        AssertAssignmentValueIn(
            governanceSource,
            "decisionState",
            ["blockingReasons.Count == 0 ? \"ready\" : \"blocked\""],
            StablePublishReadiness,
            "build/Build.Governance.Release.cs");
        AssertSourceContains(governanceSource, "if (string.Equals(decisionState, \"blocked\", StringComparison.Ordinal))", StablePublishReadiness, "build/Build.Governance.Release.cs");
        AssertSourceContains(governanceSource, "Release orchestration governance blocked publication", StablePublishReadiness, "build/Build.Governance.Release.cs");
        AssertSourceContains(governanceSource, "DistributionReadinessGovernanceReportFile", StablePublishReadiness, "build/Build.Governance.Release.cs");
        AssertSourceContains(governanceSource, "AdoptionReadinessGovernanceReportFile", StablePublishReadiness, "build/Build.Governance.Release.cs");

        AssertTargetDependsOnContainsAll(
            mainSource,
            "Ci",
            ["ReleaseOrchestrationGovernance"],
            StablePublishReadiness,
            "build/Build.cs");
        AssertTargetDependsOnContainsAll(
            mainSource,
            "CiPublish",
            ["Ci", "Publish"],
            StablePublishReadiness,
            "build/Build.cs");
    }

    [Fact]
    public void Ci_evidence_v2_includes_distribution_and_adoption_readiness_sections()
    {
        const string artifactPath = "artifacts/test-results/closeout-snapshot.json";
        using var doc = JsonDocument.Parse(
            """
            {
              "schemaVersion": 2,
              "distributionReadiness": {
                "state": "pass",
                "isStableRelease": false,
                "version": "0.1.0-preview.1",
                "failureCount": 0
              },
              "distributionReadinessFailures": [],
              "adoptionReadiness": {
                "state": "warn",
                "blockingFindingCount": 0,
                "advisoryFindingCount": 1
              },
              "adoptionBlockingFindings": [],
              "adoptionAdvisoryFindings": [
                {
                  "policyTier": "advisory",
                  "category": "adoption-docs",
                  "invariantId": "GOV-033",
                  "sourceArtifact": "README.md",
                  "expected": "phase marker present",
                  "actual": "phase marker missing"
                }
              ]
            }
            """);

        var distribution = RequireDistributionReadinessSummary(doc.RootElement, ReleaseEvidenceReadinessSections, artifactPath);
        Assert.Equal("pass", distribution.GetProperty("state").GetString());

        var adoption = RequireAdoptionReadinessSummary(doc.RootElement, ReleaseEvidenceReadinessSections, artifactPath);
        Assert.Equal("warn", adoption.GetProperty("state").GetString());

        var distributionFailures = RequireReadinessFindingsArray(doc.RootElement, "distributionReadinessFailures", DistributionReadinessSchema, artifactPath);
        Assert.Empty(distributionFailures.EnumerateArray());

        var adoptionAdvisories = RequireReadinessFindingsArray(doc.RootElement, "adoptionAdvisoryFindings", AdoptionReadinessSchema, artifactPath);
        var advisory = adoptionAdvisories.EnumerateArray().First();
        AssertAdoptionReadinessFinding(advisory, AdoptionReadinessSchema, artifactPath);
    }

    [Fact]
    public void Adoption_readiness_policy_tier_is_structured_and_deterministic()
    {
        const string artifactPath = "artifacts/test-results/adoption-readiness-governance-report.json";
        using var validDoc = JsonDocument.Parse(
            """
            {
              "summary": {
                "state": "fail"
              },
              "blockingFindings": [
                {
                  "policyTier": "blocking",
                  "category": "adoption-runtime",
                  "invariantId": "GOV-033",
                  "sourceArtifact": "artifacts/test-results/runtime-critical-path-governance-report.json",
                  "expected": "failureCount = 0",
                  "actual": "failureCount = 1"
                }
              ],
              "advisoryFindings": []
            }
            """);

        var blockingFindings = RequireReadinessFindingsArray(validDoc.RootElement, "blockingFindings", AdoptionReadinessPolicy, artifactPath);
        Assert.Single(blockingFindings.EnumerateArray());
        AssertAdoptionReadinessFinding(blockingFindings.EnumerateArray().First(), AdoptionReadinessPolicy, artifactPath);

        using var invalidDoc = JsonDocument.Parse(
            """
            {
              "blockingFindings": [
                {
                  "policyTier": "optional",
                  "category": "adoption-runtime",
                  "invariantId": "GOV-033",
                  "sourceArtifact": "artifacts/test-results/runtime-critical-path-governance-report.json",
                  "expected": "failureCount = 0",
                  "actual": "failureCount = 1"
                }
              ]
            }
            """);
        var invalidFindings = RequireReadinessFindingsArray(invalidDoc.RootElement, "blockingFindings", AdoptionReadinessPolicy, artifactPath);
        Assert.Throws<GovernanceInvariantViolationException>(() =>
            AssertAdoptionReadinessFinding(invalidFindings.EnumerateArray().First(), AdoptionReadinessPolicy, artifactPath));
    }

    [Fact]
    public void Pack_target_depends_on_test_completion()
    {
        var repoRoot = FindRepoRoot();
        var combinedSource = ReadCombinedBuildSource(repoRoot);

        AssertTargetDependsOnContainsAll(
            combinedSource,
            "Pack",
            ["Coverage", "AutomationLaneReport"],
            TestBeforePackOrdering,
            "build/Build.Packaging.cs");
    }

    [Fact]
    public void UpdateVersion_target_exists_for_manual_version_management()
    {
        var repoRoot = FindRepoRoot();
        var combinedSource = ReadCombinedBuildSource(repoRoot);

        AssertTargetDeclarationExists(combinedSource, "UpdateVersion", VersionManagementTarget, "build/Build.Versioning.cs");
        AssertSourceContains(combinedSource, "VersionPrefix", VersionManagementTarget, "build/Build.Versioning.cs");
        AssertSourceContains(combinedSource, "Directory.Build.props", VersionManagementTarget, "build/Build.Versioning.cs");
    }

    [Fact]
    public void Docs_deployment_is_inline_in_unified_workflow()
    {
        var repoRoot = FindRepoRoot();
        var ciWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
        var standaloneDocsPath = Path.Combine(repoRoot, ".github", "workflows", "docs-deploy.yml");

        Assert.False(File.Exists(standaloneDocsPath),
            $"[{DocsWorkflowCallableOnly}] docs-deploy.yml should be removed; docs deployment is inline in ci.yml.");

        var ciWorkflow = File.ReadAllText(ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "deploy-docs", DocsWorkflowCallableOnly, ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "docfx", DocsWorkflowCallableOnly, ciWorkflowPath);
        AssertSourceContains(ciWorkflow, "deploy-pages", DocsWorkflowCallableOnly, ciWorkflowPath);
    }

    [Fact]
    public void Workflow_actions_use_node24_compatible_versions_and_force_env()
    {
        var repoRoot = FindRepoRoot();
        var workflowFiles = new[]
        {
            Path.Combine(repoRoot, ".github", "workflows", "ci.yml"),
            Path.Combine(repoRoot, ".github", "workflows", "mutation-testing.yml")
        };

        var minimumMajorVersions = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["actions/checkout"] = 5,
            ["actions/setup-node"] = 6,
            ["actions/setup-dotnet"] = 5,
            ["actions/upload-artifact"] = 6,
            ["actions/download-artifact"] = 7,
            ["actions/upload-pages-artifact"] = 4,
            ["actions/deploy-pages"] = 4
        };

        var actionVersionPattern = new Regex(
            @"uses:\s*(?<action>actions/[\w-]+)@v(?<major>\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        foreach (var workflowPath in workflowFiles)
        {
            if (!File.Exists(workflowPath))
                continue;

            var content = File.ReadAllText(workflowPath);
            var relativePath = Path.GetRelativePath(repoRoot, workflowPath);

            foreach (Match match in actionVersionPattern.Matches(content))
            {
                var action = match.Groups["action"].Value;
                var major = int.Parse(match.Groups["major"].Value);

                if (!minimumMajorVersions.TryGetValue(action, out var requiredMinimum))
                    continue;

                Assert.True(major >= requiredMinimum,
                    $"[{WorkflowNode24Compatibility}] {relativePath}: {action}@v{major} is below minimum v{requiredMinimum}.");
            }

            Assert.Contains("FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true", content,
                StringComparison.Ordinal);
        }
    }

    private static string ReadCombinedBuildSource(string repoRoot)
    {
        var buildDir = Path.Combine(repoRoot, "build");
        var buildFiles = Directory.GetFiles(buildDir, "Build*.cs");
        Assert.True(buildFiles.Length >= 2, $"Expected multiple Build*.cs partial files in {buildDir}, found {buildFiles.Length}.");
        return string.Join("\n", buildFiles.Select(File.ReadAllText));
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ReadTargetDependencyGraph(string source)
    {
        var matches = Regex.Matches(
            source,
            @"Target\s+(?<target>[A-Za-z_][A-Za-z0-9_]*)\s*=>[\s\S]*?\.DependsOn\((?<deps>[\s\S]*?)\);",
            RegexOptions.Multiline);

        var graph = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (Match match in matches)
        {
            var targetName = match.Groups["target"].Value;
            var depsBlock = match.Groups["deps"].Value;
            graph[targetName] = ParseDependencyList(depsBlock);
        }

        return graph;
    }

    private static IReadOnlySet<string> ReadTargetDependencyClosure(
        IReadOnlyDictionary<string, IReadOnlySet<string>> graph,
        string targetName)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(targetName);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!graph.TryGetValue(current, out var dependencies))
                continue;

            foreach (var dependency in dependencies)
            {
                if (!closure.Add(dependency))
                    continue;

                queue.Enqueue(dependency);
            }
        }

        return closure;
    }

    private static IReadOnlySet<string> ParseDependencyList(string dependsOnBlock)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var segments = dependsOnBlock.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var token = segment.Trim();
            if (token.StartsWith("nameof(", StringComparison.Ordinal) && token.EndsWith(')'))
                token = token[7..^1];

            var identifierMatch = Regex.Match(token, @"([A-Za-z_][A-Za-z0-9_]*)$");
            if (identifierMatch.Success)
                dependencies.Add(identifierMatch.Groups[1].Value);
        }

        return dependencies;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Agibuild.Fulora.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static string? ExtractPackageVersion(string csprojXml, string packageId)
    {
        var attrPattern = new Regex(
            $@"<PackageReference\s+[^>]*Include=""{Regex.Escape(packageId)}""[^>]*\s+Version=""(?<v>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var attrMatch = attrPattern.Match(csprojXml);
        if (attrMatch.Success)
            return attrMatch.Groups["v"].Value.Trim();

        var elementPattern = new Regex(
            $@"<PackageReference\s+[^>]*Include=""{Regex.Escape(packageId)}""[^>]*>[\s\S]*?<Version>(?<v>[^<]+)</Version>[\s\S]*?</PackageReference>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var elementMatch = elementPattern.Match(csprojXml);
        if (elementMatch.Success)
            return elementMatch.Groups["v"].Value.Trim();

        var refPattern = new Regex(
            $@"<PackageReference\s+[^>]*Include=""{Regex.Escape(packageId)}""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        if (refPattern.IsMatch(csprojXml))
            return ExtractCpmVersion(packageId);

        return null;
    }

    private static string? ExtractCpmVersion(string packageId)
    {
        var propsPath = Path.Combine(FindRepoRoot(), "Directory.Packages.props");
        if (!File.Exists(propsPath))
            return null;

        var propsXml = File.ReadAllText(propsPath);
        var pattern = new Regex(
            $@"<PackageVersion\s+[^>]*Include=""{Regex.Escape(packageId)}""[^>]*\s+Version=""(?<v>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var match = pattern.Match(propsXml);
        return match.Success ? match.Groups["v"].Value.Trim() : null;
    }

    private static void AssertSingleVersion(string packageId, IReadOnlyDictionary<string, string> versionsByProject)
    {
        var distinct = versionsByProject
            .Select(kvp => kvp.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinct.Count == 1)
            return;

        var details = string.Join(
            Environment.NewLine,
            versionsByProject.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(kvp => $"{kvp.Key}: {kvp.Value}"));

        Assert.Fail($"[{XunitVersionAlignment}] Package version drift detected for '{packageId}'.\n{details}");
    }

}
