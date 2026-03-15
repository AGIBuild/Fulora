using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    private static readonly string[] BridgeDistributionPackageManagers = ["pnpm", "yarn"];

    internal Target BridgeDistributionGovernance => _ => _
        .Description("Validates @agibuild/bridge npm package builds and imports across package managers and Node LTS.")
        .Executes(async () =>
        {
            await RunGovernanceCheckAsync(
                "Bridge distribution governance",
                BridgeDistributionGovernanceReportFile,
                async () =>
                {
                    var bridgeDir = RootDirectory / "packages" / "bridge";
                    var distIndex = bridgeDir / "dist" / "index.js";
                    var checks = new List<object>();
                    var failures = new List<string>();

                    var nodeVersion = (await RunProcessCaptureAllAsync("node", ["--version"], workingDirectory: RootDirectory, timeout: TimeSpan.FromSeconds(10))).Trim();

                    try
                    {
                        await RunNpmCaptureAllAsync(["install"], bridgeDir, TimeSpan.FromMinutes(2));
                        await RunNpmCaptureAllAsync(["run", "build"], bridgeDir, TimeSpan.FromMinutes(1));
                        checks.Add(new { manager = "npm", phase = "install+build", passed = true });
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"npm install+build failed: {ex.Message}");
                        checks.Add(new { manager = "npm", phase = "install+build", passed = false, error = ex.Message });
                    }

                    foreach (var pm in BridgeDistributionPackageManagers)
                    {
                        var available = await IsToolAvailableAsync(pm);
                        if (!available)
                        {
                            checks.Add(new { manager = pm, phase = "consume-smoke", passed = true, skipped = true, reason = $"{pm} not installed" });
                            Serilog.Log.Warning("Skipping {Pm} parity check — tool not found on PATH.", pm);
                            continue;
                        }

                        try
                        {
                            using var tempDirScope = TempDirectoryScope.Create($"bridge-{pm}-smoke");
                            var tempDir = (AbsolutePath)tempDirScope.Path;

                            var packageJson = $$"""
                                {
                                  "name": "bridge-{{pm}}-smoke",
                                  "version": "1.0.0",
                                  "private": true,
                                  "type": "module",
                                  "dependencies": {
                                    "@agibuild/bridge": "file:{{bridgeDir.ToString().Replace("\\", "/")}}"
                                  }
                                }
                                """;
                            File.WriteAllText(tempDir / "package.json", packageJson);

                            if (string.Equals(pm, "yarn", StringComparison.OrdinalIgnoreCase))
                                File.WriteAllText(tempDir / ".yarnrc.yml", "nodeLinker: node-modules\n");

                            var consumerScript = """
                                import { createBridgeClient } from '@agibuild/bridge';
                                if (typeof createBridgeClient !== 'function') process.exit(1);
                                console.log('SMOKE_PASSED');
                                """;
                            File.WriteAllText(tempDir / "consumer.mjs", consumerScript);

                            await RunPmInstallAsync(pm, tempDir);
                            var output = await RunProcessCaptureAllAsync("node", ["consumer.mjs"], workingDirectory: tempDir, timeout: TimeSpan.FromSeconds(30));
                            var passed = output.Contains("SMOKE_PASSED", StringComparison.Ordinal);
                            checks.Add(new { manager = pm, phase = "consume-smoke", passed, output = passed ? null : output });
                            if (!passed)
                                failures.Add($"{pm} consume smoke did not produce SMOKE_PASSED. Output: {output}");
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"{pm} consume smoke failed: {ex.Message}");
                            checks.Add(new { manager = pm, phase = "consume-smoke", passed = false, error = ex.Message });
                        }
                    }

                    if (File.Exists(distIndex))
                    {
                        try
                        {
                            using var ltsDirScope = TempDirectoryScope.Create("bridge-lts-smoke");
                            var ltsDir = (AbsolutePath)ltsDirScope.Path;

                            var fileUrl = new Uri(distIndex).AbsoluteUri;
                            var ltsScript = $"""
                                const b = await import('{fileUrl}');
                                if (typeof b.createBridgeClient !== 'function') process.exit(1);
                                console.log('LTS_IMPORT_OK');
                                """;
                            File.WriteAllText(ltsDir / "check.mjs", ltsScript);

                            var importCheck = await RunProcessCaptureAllAsync(
                                "node", ["check.mjs"],
                                workingDirectory: ltsDir,
                                timeout: TimeSpan.FromSeconds(10));
                            var passed = importCheck.Contains("LTS_IMPORT_OK", StringComparison.Ordinal);
                            checks.Add(new { phase = "node-lts-import", nodeVersion, passed, output = passed ? null : importCheck, scriptContent = passed ? null : File.ReadAllText(ltsDir / "check.mjs") });
                            if (!passed)
                                failures.Add($"Node LTS import check failed on {nodeVersion}. Output: {importCheck}");
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"Node LTS import check failed: {ex.Message}");
                            checks.Add(new { phase = "node-lts-import", nodeVersion, passed = false, error = ex.Message });
                        }
                    }
                    else
                    {
                        failures.Add($"Bridge dist/index.js not found at {distIndex}. Build may have failed.");
                        checks.Add(new { phase = "node-lts-import", nodeVersion, passed = false, error = "dist/index.js not found" });
                    }

                    var reportPayload = new
                    {
                        schemaVersion = 2,
                        provenance = new
                        {
                            laneContext = LaneContextCi,
                            producerTarget = "BridgeDistributionGovernance",
                            timestamp = DateTime.UtcNow.ToString("o")
                        },
                        nodeVersion,
                        checks,
                        failureCount = failures.Count,
                        failures
                    };

                    return new GovernanceCheckResult(failures, reportPayload);
                });
        });

    internal Target DistributionReadinessGovernance => _ => _
        .Description("Evaluates deterministic package/distribution readiness before release orchestration.")
        .DependsOn(ValidatePackage)
        .Executes(() =>
        {
            TestResultsDirectory.CreateDirectory();

            var failures = new List<GovernanceFailure>();
            void AddFailure(string category, string invariantId, string sourceArtifact, string expected, string actual)
                => failures.Add(new GovernanceFailure(category, invariantId, sourceArtifact, expected, actual));

            var canonicalPackageIds = new[]
            {
                PrimaryHostPackageId,
                "Agibuild.Fulora.Core",
                "Agibuild.Fulora.Runtime",
                "Agibuild.Fulora.Adapters.Abstractions",
                "Agibuild.Fulora.Bridge.Generator",
                "Agibuild.Fulora.Testing"
            };

            foreach (var packageId in canonicalPackageIds)
            {
                var hasPackage = PackageOutputDirectory
                    .GlobFiles($"{packageId}.*.nupkg")
                    .Any(path => !path.Name.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase));
                if (!hasPackage)
                {
                    AddFailure(
                        category: "package-metadata",
                        invariantId: DistributionReadinessDecisionInvariantId,
                        sourceArtifact: "artifacts/packages",
                        expected: $"canonical package '{packageId}' exists",
                        actual: "missing");
                }
            }

            var changelogPath = RootDirectory / "CHANGELOG.md";
            if (!File.Exists(changelogPath))
            {
                AddFailure(
                    category: "evidence",
                    invariantId: DistributionReadinessSchemaInvariantId,
                    sourceArtifact: "CHANGELOG.md",
                    expected: "changelog file exists",
                    actual: "missing");
            }
            else
            {
                var changelog = File.ReadAllText(changelogPath);
                if (!changelog.Contains("## [1.0.0]", StringComparison.Ordinal))
                {
                    AddFailure(
                        category: "evidence",
                        invariantId: DistributionReadinessSchemaInvariantId,
                        sourceArtifact: "CHANGELOG.md",
                        expected: "Keep-a-Changelog section for [1.0.0] present",
                        actual: "section missing");
                }
            }

            string? packedVersion = null;
            try
            {
                packedVersion = ResolvePackedAgibuildVersion(PrimaryHostPackageId);
            }
            catch (Exception ex)
            {
                AddFailure(
                    category: "package-metadata",
                    invariantId: DistributionReadinessDecisionInvariantId,
                    sourceArtifact: "artifacts/packages",
                    expected: "packed version resolved for Agibuild.Fulora.Avalonia",
                    actual: ex.Message);
            }

            var isStableRelease = packedVersion is not null && !packedVersion.Contains('-', StringComparison.Ordinal);
            if (isStableRelease)
            {
                var mainPackagePrefix = $"{PrimaryHostPackageId}.";
                var mainPackage = PackageOutputDirectory
                    .GlobFiles($"{PrimaryHostPackageId}.*.nupkg")
                    .Where(path => !path.Name.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(path =>
                        path.Name.StartsWith(mainPackagePrefix, StringComparison.OrdinalIgnoreCase)
                        && path.Name.Length > mainPackagePrefix.Length
                        && char.IsDigit(path.Name[mainPackagePrefix.Length]));

                if (mainPackage is null)
                {
                    AddFailure(
                        category: "package-metadata",
                        invariantId: StablePublishReadinessInvariantId,
                        sourceArtifact: "artifacts/packages",
                        expected: "stable main package nupkg exists",
                        actual: "missing");
                }
                else
                {
                    using var archive = System.IO.Compression.ZipFile.OpenRead(mainPackage);
                    var nuspecEntry = archive.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                    if (nuspecEntry is null)
                    {
                        AddFailure(
                            category: "package-metadata",
                            invariantId: StablePublishReadinessInvariantId,
                            sourceArtifact: mainPackage.Name,
                            expected: "nuspec exists in package",
                            actual: "missing");
                    }
                    else
                    {
                        using var stream = nuspecEntry.Open();
                        var nuspecDoc = System.Xml.Linq.XDocument.Load(stream);
                        var ns = nuspecDoc.Root?.GetDefaultNamespace() ?? System.Xml.Linq.XNamespace.None;
                        var metadata = nuspecDoc.Root?.Element(ns + "metadata");

                        var id = metadata?.Element(ns + "id")?.Value;
                        var description = metadata?.Element(ns + "description")?.Value;
                        var licenseExpr = metadata?.Element(ns + "license")?.Value;
                        var projectUrl = metadata?.Element(ns + "projectUrl")?.Value;

                        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("Agibuild.Fulora.", StringComparison.Ordinal))
                        {
                            AddFailure(
                                category: "package-metadata",
                                invariantId: StablePublishReadinessInvariantId,
                                sourceArtifact: mainPackage.Name,
                                expected: "stable package id uses canonical Agibuild.Fulora prefix",
                                actual: id ?? "<null>");
                        }

                        if (!string.Equals(id, PrimaryHostPackageId, StringComparison.Ordinal))
                        {
                            AddFailure(
                                category: "package-metadata",
                                invariantId: StablePublishReadinessInvariantId,
                                sourceArtifact: mainPackage.Name,
                                expected: $"primary host package id = {PrimaryHostPackageId}",
                                actual: id ?? "<null>");
                        }

                        if (string.IsNullOrWhiteSpace(licenseExpr))
                        {
                            AddFailure(
                                category: "package-metadata",
                                invariantId: StablePublishReadinessInvariantId,
                                sourceArtifact: mainPackage.Name,
                                expected: "stable package license expression present",
                                actual: "missing");
                        }

                        if (string.IsNullOrWhiteSpace(projectUrl))
                        {
                            AddFailure(
                                category: "package-metadata",
                                invariantId: StablePublishReadinessInvariantId,
                                sourceArtifact: mainPackage.Name,
                                expected: "stable package projectUrl present",
                                actual: "missing");
                        }

                        if (!string.IsNullOrWhiteSpace(description)
                            && (description.Contains("preview", StringComparison.OrdinalIgnoreCase)
                                || description.Contains("pre-release", StringComparison.OrdinalIgnoreCase)))
                        {
                            AddFailure(
                                category: "package-metadata",
                                invariantId: StablePublishReadinessInvariantId,
                                sourceArtifact: mainPackage.Name,
                                expected: "stable package description has no preview language",
                                actual: description);
                        }
                    }
                }
            }

            var state = failures.Count == 0 ? "pass" : "fail";
            var evaluatedAtUtc = DateTime.UtcNow.ToString("o");
            var reportPayload = new
            {
                schemaVersion = 1,
                provenance = new
                {
                    laneContext = LaneContextCi,
                    producerTarget = "DistributionReadinessGovernance",
                    timestamp = evaluatedAtUtc
                },
                summary = new
                {
                    state,
                    isStableRelease,
                    version = packedVersion ?? "unknown",
                    failureCount = failures.Count
                },
                failures = failures.Select(f => new
                {
                    category = f.Category,
                    invariantId = f.InvariantId,
                    sourceArtifact = f.SourceArtifact,
                    expected = f.Expected,
                    actual = f.Actual
                }).ToArray()
            };

            WriteJsonReport(DistributionReadinessGovernanceReportFile, reportPayload);
            Serilog.Log.Information("Distribution readiness governance report written to {Path}", DistributionReadinessGovernanceReportFile);

            if (failures.Count > 0)
            {
                var lines = failures.Select(f =>
                    $"- [{f.Category}] [{f.InvariantId}] {f.SourceArtifact}: expected {f.Expected}, actual {f.Actual}");
                Assert.Fail("Distribution readiness governance failed:\n" + string.Join('\n', lines));
            }
        });

    internal Target AdoptionReadinessGovernance => _ => _
        .Description("Evaluates adoption-readiness signals.")
        .DependsOn(AutomationLaneReport, RuntimeCriticalPathExecutionGovernance)
        .Executes(() => EvaluateAdoptionReadiness(
            laneContext: LaneContextCi,
            producerTarget: "AdoptionReadinessGovernance",
            expectedRuntimeLaneContext: LaneContextCi));

    private static void EvaluateAdoptionReadiness(string laneContext, string producerTarget, string expectedRuntimeLaneContext)
    {
        TestResultsDirectory.CreateDirectory();

        var blockingFindings = new List<GovernanceFailure>();
        var advisoryFindings = new List<GovernanceFailure>();

        void AddBlocking(string category, string invariantId, string sourceArtifact, string expected, string actual) =>
            blockingFindings.Add(new GovernanceFailure(category, invariantId, sourceArtifact, expected, actual));
        void AddAdvisory(string category, string invariantId, string sourceArtifact, string expected, string actual) =>
            advisoryFindings.Add(new GovernanceFailure(category, invariantId, sourceArtifact, expected, actual));

        var readmePath = RootDirectory / "README.md";
        if (!File.Exists(readmePath))
        {
            AddBlocking(
                category: "adoption-docs",
                invariantId: AdoptionReadinessSchemaInvariantId,
                sourceArtifact: "README.md",
                expected: "README exists",
                actual: "missing");
        }
        else
        {
            var readme = File.ReadAllText(readmePath);
            if (!readme.Contains("Roadmap Alignment", StringComparison.Ordinal))
            {
                AddBlocking(
                    category: "adoption-docs",
                    invariantId: AdoptionReadinessSchemaInvariantId,
                    sourceArtifact: "README.md",
                    expected: "Roadmap Alignment section present",
                    actual: "section missing");
            }

            if (!readme.Contains("Phase 7", StringComparison.Ordinal))
            {
                AddAdvisory(
                    category: "adoption-docs",
                    invariantId: AdoptionReadinessPolicyInvariantId,
                    sourceArtifact: "README.md",
                    expected: "Phase 7 roadmap line is surfaced",
                    actual: "phase marker missing");
            }
        }

        var templateConfigPath = RootDirectory / "templates" / "agibuild-hybrid" / ".template.config" / "template.json";
        var appShellPresetPath = RootDirectory / "templates" / "agibuild-hybrid" / "HybridApp.Desktop" / "MainWindow.AppShellPreset.cs";
        if (!File.Exists(templateConfigPath))
        {
            AddBlocking(
                category: "adoption-template",
                invariantId: AdoptionReadinessSchemaInvariantId,
                sourceArtifact: "templates/agibuild-hybrid/.template.config/template.json",
                expected: "template configuration exists",
                actual: "missing");
        }
        if (!File.Exists(appShellPresetPath))
        {
            AddBlocking(
                category: "adoption-template",
                invariantId: AdoptionReadinessSchemaInvariantId,
                sourceArtifact: "templates/agibuild-hybrid/HybridApp.Desktop/MainWindow.AppShellPreset.cs",
                expected: "app-shell preset implementation exists",
                actual: "missing");
        }

        if (!File.Exists(AutomationLaneReportFile))
        {
            AddBlocking(
                category: "adoption-runtime",
                invariantId: AdoptionReadinessSchemaInvariantId,
                sourceArtifact: "artifacts/test-results/automation-lane-report.json",
                expected: "automation lane report exists",
                actual: "missing");
        }

        if (!File.Exists(RuntimeCriticalPathGovernanceReportFile))
        {
            AddBlocking(
                category: "adoption-runtime",
                invariantId: AdoptionReadinessSchemaInvariantId,
                sourceArtifact: "artifacts/test-results/runtime-critical-path-governance-report.json",
                expected: "runtime critical-path governance report exists",
                actual: "missing");
        }
        else
        {
            using var runtimeDoc = JsonDocument.Parse(File.ReadAllText(RuntimeCriticalPathGovernanceReportFile));
            var root = runtimeDoc.RootElement;
            var failureCount = root.TryGetProperty("failureCount", out var failureNode) && failureNode.ValueKind == JsonValueKind.Number
                ? failureNode.GetInt32()
                : -1;
            if (failureCount > 0)
            {
                AddBlocking(
                    category: "adoption-runtime",
                    invariantId: AdoptionReadinessPolicyInvariantId,
                    sourceArtifact: "artifacts/test-results/runtime-critical-path-governance-report.json",
                    expected: "failureCount = 0",
                    actual: $"failureCount = {failureCount}");
            }

            var runtimeLaneContext = root.TryGetProperty("provenance", out var provenanceNode)
                                     && provenanceNode.TryGetProperty("laneContext", out var laneContextNode)
                ? laneContextNode.GetString()
                : null;
            if (!string.Equals(runtimeLaneContext, expectedRuntimeLaneContext, StringComparison.Ordinal))
            {
                AddBlocking(
                    category: "adoption-runtime",
                    invariantId: AdoptionReadinessPolicyInvariantId,
                    sourceArtifact: "artifacts/test-results/runtime-critical-path-governance-report.json",
                    expected: $"provenance.laneContext = {expectedRuntimeLaneContext}",
                    actual: $"provenance.laneContext = {runtimeLaneContext ?? "<null>"}");
            }
        }

        var state = blockingFindings.Count > 0
            ? "fail"
            : advisoryFindings.Count > 0
                ? "warn"
                : "pass";
        var evaluatedAtUtc = DateTime.UtcNow.ToString("o");

        var reportPayload = new
        {
            schemaVersion = 1,
            provenance = new
            {
                laneContext,
                producerTarget,
                timestamp = evaluatedAtUtc
            },
            summary = new
            {
                state,
                blockingFindingCount = blockingFindings.Count,
                advisoryFindingCount = advisoryFindings.Count
            },
            blockingFindings = blockingFindings.Select(f => new
            {
                policyTier = "blocking",
                category = f.Category,
                invariantId = f.InvariantId,
                sourceArtifact = f.SourceArtifact,
                expected = f.Expected,
                actual = f.Actual
            }).ToArray(),
            advisoryFindings = advisoryFindings.Select(f => new
            {
                policyTier = "advisory",
                category = f.Category,
                invariantId = f.InvariantId,
                sourceArtifact = f.SourceArtifact,
                expected = f.Expected,
                actual = f.Actual
            }).ToArray()
        };

        WriteJsonReport(AdoptionReadinessGovernanceReportFile, reportPayload);
        Serilog.Log.Information("Adoption readiness governance report written to {Path}", AdoptionReadinessGovernanceReportFile);

        if (blockingFindings.Count > 0)
        {
            var lines = blockingFindings.Select(f =>
                $"- [{f.Category}] [{f.InvariantId}] {f.SourceArtifact}: expected {f.Expected}, actual {f.Actual}");
            Assert.Fail("Adoption readiness governance failed:\n" + string.Join('\n', lines));
        }
    }
}
