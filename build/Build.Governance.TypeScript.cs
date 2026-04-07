using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;

internal partial class BuildTask
{
    private const string TypeScriptDeclarationInvariantId = "GOV-038";

    private static readonly string[] TypeScriptGovernanceGovernedFiles =
    [
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
    ];

    private static readonly string[] TypeScriptGovernanceProhibitedMarkers =
    [
        "window.agWebView",
        ".rpc.invoke(",
        "bridgeClient.getService",
        "EnableSpaHosting(",
        "BootstrapSpaAsync(",
        "WebView.NavigateAsync("
    ];

    internal Target TypeScriptDeclarationGovernance => _ => _
        .Description("Validates TypeScript declaration generation and DX package wiring contracts.")
        .Executes(() =>
        {
            RunGovernanceCheck(
                "TypeScript declaration governance",
                TypeScriptGovernanceReportFile,
                () =>
                {
                    var failures = new List<GovernanceFailure>();
                    var checks = new List<object>();
                    var semanticDiagnostics = new List<object>();
                    const string strictScope = "official-maintained-app-layer::strict-no-exception";

                    void AddSemanticViolation(string artifactPath, string expected, string actual)
                    {
                        semanticDiagnostics.Add(new
                        {
                            invariantId = BridgeSingleEntryAppLayerPolicyInvariantId,
                            artifactPath,
                            expected,
                            actual,
                            scope = strictScope,
                            decision = "deny"
                        });
                        failures.Add(new GovernanceFailure(
                            Category: "typescript-semantic",
                            InvariantId: BridgeSingleEntryAppLayerPolicyInvariantId,
                            SourceArtifact: artifactPath,
                            Expected: expected,
                            Actual: actual));
                    }

                    var targetsPath = RootDirectory / "src" / "Agibuild.Fulora.Bridge.Generator" / "build" / "Agibuild.Fulora.Bridge.Generator.targets";
                    if (!File.Exists(targetsPath))
                    {
                        failures.Add(new GovernanceFailure(
                            Category: "typescript-declaration",
                            InvariantId: TypeScriptDeclarationInvariantId,
                            SourceArtifact: targetsPath.ToString(),
                            Expected: "bridge generator targets file exists",
                            Actual: "missing"));
                    }
                    else
                    {
                        var targets = File.ReadAllText(targetsPath);
                        var hasGenerateFlag = targets.Contains("GenerateBridgeTypeScript", StringComparison.Ordinal);
                        var hasOutputDir = targets.Contains("BridgeTypeScriptOutputDir", StringComparison.Ordinal);
                        var hasBridgeDts = targets.Contains("bridge.d.ts", StringComparison.Ordinal);

                        checks.Add(new
                        {
                            file = targetsPath.ToString(),
                            hasGenerateFlag,
                            hasOutputDir,
                            hasBridgeDts
                        });

                        if (!hasGenerateFlag || !hasOutputDir || !hasBridgeDts)
                            failures.Add(new GovernanceFailure(
                                Category: "typescript-declaration",
                                InvariantId: TypeScriptDeclarationInvariantId,
                                SourceArtifact: targetsPath.ToString(),
                                Expected: "bridge.d.ts generation wiring complete",
                                Actual: "missing required wiring"));
                    }

                    var packageEntryPath = RootDirectory / "packages" / "bridge" / "src" / "index.ts";
                    var packageConfigPath = RootDirectory / "packages" / "bridge" / "package.json";
                    checks.Add(new
                    {
                        file = packageEntryPath.ToString(),
                        exists = File.Exists(packageEntryPath)
                    });
                    checks.Add(new
                    {
                        file = packageConfigPath.ToString(),
                        exists = File.Exists(packageConfigPath)
                    });
                    if (!File.Exists(packageEntryPath) || !File.Exists(packageConfigPath))
                        failures.Add(new GovernanceFailure(
                            Category: "typescript-declaration",
                            InvariantId: TypeScriptDeclarationInvariantId,
                            SourceArtifact: "packages/bridge",
                            Expected: "npm package source complete",
                            Actual: "incomplete"));

                    var vueTsconfigPath = RootDirectory / "samples" / "avalonia-vue" / "AvaloniVue.Web" / "tsconfig.json";
                    if (!File.Exists(vueTsconfigPath))
                    {
                        failures.Add(new GovernanceFailure(
                            Category: "typescript-declaration",
                            InvariantId: TypeScriptDeclarationInvariantId,
                            SourceArtifact: vueTsconfigPath.ToString(),
                            Expected: "Vue sample tsconfig exists",
                            Actual: "missing"));
                    }
                    else
                    {
                        var vueTsconfig = File.ReadAllText(vueTsconfigPath);
                        var referencesBridgeDeclaration = vueTsconfig.Contains("bridge.d.ts", StringComparison.Ordinal);
                        checks.Add(new
                        {
                            file = vueTsconfigPath.ToString(),
                            referencesBridgeDeclaration
                        });
                        if (!referencesBridgeDeclaration)
                            failures.Add(new GovernanceFailure(
                                Category: "typescript-declaration",
                                InvariantId: TypeScriptDeclarationInvariantId,
                                SourceArtifact: vueTsconfigPath.ToString(),
                                Expected: "tsconfig includes generated bridge.d.ts",
                                Actual: "reference missing"));
                    }

                    foreach (var relativePath in TypeScriptGovernanceGovernedFiles)
                    {
                        var absolutePath = RootDirectory / relativePath.Replace('/', Path.DirectorySeparatorChar);
                        if (!File.Exists(absolutePath))
                        {
                            AddSemanticViolation(relativePath, "file exists", "file missing");
                            continue;
                        }

                        var source = File.ReadAllText(absolutePath);
                        checks.Add(new
                        {
                            file = relativePath,
                            scope = strictScope,
                            mode = "official-sample-template-strict"
                        });

                        foreach (var marker in TypeScriptGovernanceProhibitedMarkers)
                        {
                            if (source.Contains(marker, StringComparison.Ordinal))
                            {
                                AddSemanticViolation(
                                    relativePath,
                                    $"marker absent '{marker}' (scope={strictScope}, decision=deny)",
                                    $"marker present '{marker}' (scope={strictScope}, decision=deny)");
                            }
                        }

                        if (relativePath.EndsWith("/bridge/client.ts", StringComparison.Ordinal))
                        {
                            if (!(source.Contains("@agibuild/bridge/profile", StringComparison.Ordinal)
                                  || source.Contains("@agibuild/bridge", StringComparison.Ordinal)))
                                AddSemanticViolation(relativePath, "import from '@agibuild/bridge/profile' or '@agibuild/bridge'", "bridge runtime import missing");
                            if (!(source.Contains("createBridgeProfile", StringComparison.Ordinal)
                                  || source.Contains("createBridgeClient", StringComparison.Ordinal)))
                                AddSemanticViolation(relativePath, "use createBridgeProfile or createBridgeClient", "bridge client bootstrap missing");
                        }
                        else if (relativePath.EndsWith("/bridge/services.ts", StringComparison.Ordinal))
                        {
                            if (!(source.Contains("generated/bridge.client", StringComparison.Ordinal)
                                  || source.Contains("from './client'", StringComparison.Ordinal)
                                  || source.Contains("from \"./client\"", StringComparison.Ordinal)))
                                AddSemanticViolation(relativePath, "re-export generated bridge client contracts directly or through './client'", "generated contract import missing");
                        }
                        else if (relativePath.EndsWith("useBridge.ts", StringComparison.Ordinal))
                        {
                            if (!source.Contains("bridgeProfile.ready", StringComparison.Ordinal))
                                AddSemanticViolation(relativePath, "use bridgeProfile.ready for readiness", "bridgeProfile.ready missing");
                        }
                        else if (relativePath.EndsWith("MainWindow.axaml.cs", StringComparison.Ordinal))
                        {
                            if (!source.Contains("BootstrapSpaProfileAsync", StringComparison.Ordinal))
                                AddSemanticViolation(relativePath, "use BootstrapSpaProfileAsync host entrypoint", "profile bootstrap missing");
                        }
                    }

                    var reportPayload = new
                    {
                        generatedAtUtc = DateTime.UtcNow,
                        checks,
                        semanticDiagnostics,
                        failureCount = failures.Count,
                        failures
                    };

                    return new GovernanceCheckResult(failures, reportPayload);
                });
        });
}
