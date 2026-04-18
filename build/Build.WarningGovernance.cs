using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    private static readonly JsonSerializerOptions PropertyNameCaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record WarningObservation(
        string WarningId,
        string Message,
        string SourceLine,
        string? SourceFile);

    private sealed record WarningClassification(
        string Category,
        string WarningId,
        string Message,
        string SourceLine,
        string? SourceFile,
        string Reason);

    private sealed class WarningGovernanceBaseline
    {
        public int Version { get; set; } = 1;
        public List<WindowsBaseConflictBaselineEntry> WindowsBaseConflicts { get; set; } = [];
        public List<XunitSuppressionBaselineEntry> XunitSuppressions { get; set; } = [];
    }

    private sealed class WindowsBaseConflictBaselineEntry
    {
        public string WarningId { get; set; } = string.Empty;
        public string MessageContains { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Rationale { get; set; } = string.Empty;
        public string ReviewPoint { get; set; } = string.Empty;
    }

    private sealed class XunitSuppressionBaselineEntry
    {
        public string Path { get; set; } = string.Empty;
        public string WarningId { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Rationale { get; set; } = string.Empty;
        public string ReviewPoint { get; set; } = string.Empty;
    }

    private async Task EvaluateWarningGovernanceAsync(bool failOnIssues)
    {
        TestResultsDirectory.CreateDirectory();

        var baseline = LoadWarningGovernanceBaseline();
        var touchedFiles = await GetTouchedFilesForWarningGovernanceAsync();
        var warnings = await CollectGovernedWarningsAsync();

        var classifications = ClassifyWarnings(warnings, baseline, touchedFiles);
        classifications.AddRange(ValidateBaselineMetadata(baseline));
        classifications.AddRange(EvaluateXunitSuppressionFindings(touchedFiles, baseline));

        var knownBaselineCount = classifications.Count(x => string.Equals(x.Category, "known-baseline", StringComparison.Ordinal));
        var actionableCount = classifications.Count(x => string.Equals(x.Category, "actionable", StringComparison.Ordinal));
        var newRegressionCount = classifications.Count(x => string.Equals(x.Category, "new-regression", StringComparison.Ordinal));

        var reportPayload = new
        {
            generatedAtUtc = DateTime.UtcNow,
            baselinePath = WarningGovernanceBaselineFile.ToString(),
            inputPath = WarningGovernanceInput,
            touchedFiles = touchedFiles.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            warnings = classifications.Select(x => new
            {
                category = x.Category,
                warningId = x.WarningId,
                message = x.Message,
                sourceLine = x.SourceLine,
                sourceFile = x.SourceFile,
                reason = x.Reason
            }),
            summary = new
            {
                total = classifications.Count,
                knownBaseline = knownBaselineCount,
                actionable = actionableCount,
                newRegression = newRegressionCount
            }
        };

        WriteJsonReport(WarningGovernanceReportFile, reportPayload);
        Serilog.Log.Information("Warning governance report written to {Path}", WarningGovernanceReportFile);
        Serilog.Log.Information(
            "Warning governance summary: known-baseline={KnownBaseline}, actionable={Actionable}, new-regression={NewRegression}",
            knownBaselineCount,
            actionableCount,
            newRegressionCount);

        if (failOnIssues && (actionableCount > 0 || newRegressionCount > 0))
        {
            Assert.Fail(
                $"Warning governance gate failed. actionable={actionableCount}, new-regression={newRegressionCount}. " +
                $"See {WarningGovernanceReportFile}.");
        }
    }

    private void RunSyntheticWarningGovernanceChecks()
    {
        var baseline = LoadWarningGovernanceBaseline();

        var baselineCase = ClassifyWarnings(
            new List<WarningObservation>
            {
                new(
                    "MSB3277",
                    "Found conflicts between different versions of \"WindowsBase\" that could not be resolved.",
                    "synthetic: warning MSB3277: Found conflicts between different versions of \"WindowsBase\".",
                    SourceFile: null)
            },
            baseline,
            touchedFiles: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.True(baselineCase.Any(x => x.Category is "new-regression"),
            "Synthetic WindowsBase conflict warning should classify as new-regression.");

        var regressionCase = ClassifyWarnings(
            new List<WarningObservation>
            {
                new(
                    "xUnit2012",
                    "Do not use Assert.True() to check if a value exists in a collection.",
                    "tests/Synthetic/ExampleTests.cs(10,5): warning xUnit2012: synthetic regression",
                    SourceFile: "tests/Synthetic/ExampleTests.cs")
            },
            baseline,
            touchedFiles: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "tests/Synthetic/ExampleTests.cs"
            });
        Assert.True(regressionCase.Any(x => x.Category is "actionable" or "new-regression"),
            "Synthetic regression warning should fail governance classification.");

        Serilog.Log.Information("Synthetic warning governance checks passed.");
    }

    private static WarningGovernanceBaseline LoadWarningGovernanceBaseline()
    {
        Assert.FileExists(WarningGovernanceBaselineFile, $"Missing warning governance baseline file: {WarningGovernanceBaselineFile}");
        var content = File.ReadAllText(WarningGovernanceBaselineFile);
        var baseline = JsonSerializer.Deserialize<WarningGovernanceBaseline>(
            content,
            PropertyNameCaseInsensitiveJsonOptions);
        Assert.NotNull(baseline, $"Unable to deserialize warning governance baseline: {WarningGovernanceBaselineFile}");
        return baseline!;
    }

    private async Task<List<WarningObservation>> CollectGovernedWarningsAsync()
    {
        if (!string.IsNullOrWhiteSpace(WarningGovernanceInput))
        {
            var inputPath = Path.IsPathRooted(WarningGovernanceInput)
                ? WarningGovernanceInput
                : Path.Combine(RootDirectory, WarningGovernanceInput);
            Assert.FileExists(inputPath, $"Warning governance input file does not exist: {inputPath}");
            return ParseWarningsFromOutput(File.ReadAllText(inputPath));
        }

        var buildTimeout = TimeSpan.FromMinutes(3);
        var output = string.Join(
            "\n",
            new[]
            {
                await RunProcessCaptureAllAsync("dotnet",
                    ["build", PlatformsProject, "--configuration", Configuration, "--no-restore", "--no-incremental", "--nologo", "-v", "minimal"],
                    workingDirectory: RootDirectory,
                    timeout: buildTimeout),
                await RunProcessCaptureAllAsync("dotnet",
                    ["build", PackProject, "--configuration", Configuration, "--no-restore", "--no-incremental", "--nologo", "-v", "minimal"],
                    workingDirectory: RootDirectory,
                    timeout: buildTimeout),
                await RunProcessCaptureAllAsync("dotnet",
                    ["build", IntegrationTestsProject, "--configuration", Configuration, "--no-restore", "--no-incremental", "--nologo", "-v", "minimal"],
                    workingDirectory: RootDirectory,
                    timeout: buildTimeout),
                await RunProcessCaptureAllAsync("dotnet",
                    ["build", UnitTestsProject, "--configuration", Configuration, "--no-restore", "--no-incremental", "--nologo", "-v", "minimal"],
                    workingDirectory: RootDirectory,
                    timeout: buildTimeout)
            });

        return ParseWarningsFromOutput(output);
    }

    private static List<WarningObservation> ParseWarningsFromOutput(string output)
    {
        var observations = new List<WarningObservation>();
        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            var markerIndex = line.IndexOf(": warning ", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var sourcePart = line[..markerIndex].Trim();
            var warningPart = line[(markerIndex + ": warning ".Length)..];
            var idSeparator = warningPart.IndexOf(':');
            if (idSeparator < 0)
            {
                continue;
            }

            var warningId = warningPart[..idSeparator].Trim();
            var message = warningPart[(idSeparator + 1)..].Trim();
            if (!IsGovernedWarning(warningId, message))
            {
                continue;
            }

            observations.Add(new WarningObservation(
                warningId,
                message,
                line,
                TryExtractSourceFile(sourcePart)));
        }

        return observations
            .DistinctBy(x => $"{x.WarningId}|{x.SourceLine}")
            .ToList();
    }

    private static bool IsGovernedWarning(string warningId, string message)
    {
        if (string.Equals(warningId, "MSB3277", StringComparison.OrdinalIgnoreCase)
            && message.Contains("WindowsBase", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return warningId.StartsWith("xUnit", StringComparison.OrdinalIgnoreCase);
    }

    private static List<WarningClassification> ClassifyWarnings(
        IEnumerable<WarningObservation> warnings,
        WarningGovernanceBaseline baseline,
        HashSet<string> touchedFiles)
    {
        var classifications = new List<WarningClassification>();
        foreach (var warning in warnings)
        {
            if (string.Equals(warning.WarningId, "MSB3277", StringComparison.OrdinalIgnoreCase))
            {
                classifications.Add(new WarningClassification(
                    "new-regression",
                    warning.WarningId,
                    warning.Message,
                    warning.SourceLine,
                    warning.SourceFile,
                    "WindowsBase conflict warning must be eliminated; baseline acceptance is not allowed."));
                continue;
            }

            if (warning.WarningId.StartsWith("xUnit", StringComparison.OrdinalIgnoreCase))
            {
                var sourceFile = warning.SourceFile ?? string.Empty;
                var isTouched = sourceFile.Length > 0 && touchedFiles.Contains(sourceFile);
                if (isTouched && !HasApprovedXunitSuppression(sourceFile, warning.WarningId, baseline))
                {
                    classifications.Add(new WarningClassification(
                        "actionable",
                        warning.WarningId,
                        warning.Message,
                        warning.SourceLine,
                        warning.SourceFile,
                        "Touched test file emitted xUnit analyzer warning without approved scoped suppression metadata."));
                }
                else
                {
                    classifications.Add(new WarningClassification(
                        "known-baseline",
                        warning.WarningId,
                        warning.Message,
                        warning.SourceLine,
                        warning.SourceFile,
                        isTouched
                            ? "Touched file warning has approved scoped suppression metadata."
                            : "Untouched-file xUnit warning tracked as existing baseline noise."));
                }
            }
        }

        return classifications;
    }

    private static List<WarningClassification> ValidateBaselineMetadata(WarningGovernanceBaseline baseline)
    {
        var issues = new List<WarningClassification>();

        foreach (var entry in baseline.WindowsBaseConflicts)
        {
            issues.Add(new WarningClassification(
                "actionable",
                entry.WarningId,
                entry.MessageContains,
                "baseline/windowsBaseConflicts",
                SourceFile: null,
                "WindowsBase baseline entries are not allowed after source-elimination policy."));
        }

        foreach (var entry in baseline.XunitSuppressions)
        {
            if (IsMissingMetadata(entry.Owner, entry.Rationale, entry.ReviewPoint))
            {
                issues.Add(new WarningClassification(
                    "actionable",
                    entry.WarningId,
                    entry.Path,
                    "baseline/xunitSuppressions",
                    SourceFile: NormalizeRelativePath(entry.Path, RootDirectory),
                    "xUnit suppression baseline entry is missing owner/rationale/reviewPoint metadata."));
            }
        }

        return issues;
    }

    private static List<WarningClassification> EvaluateXunitSuppressionFindings(ISet<string> touchedFiles, WarningGovernanceBaseline baseline)
    {
        var findings = new List<WarningClassification>();
        foreach (var touchedFile in touchedFiles)
        {
            if (!touchedFile.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!touchedFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !touchedFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                && !touchedFile.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = Path.Combine(RootDirectory, touchedFile.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var content = File.ReadAllText(fullPath);
            var suppressedIds = ExtractSuppressedXunitIds(content, touchedFile);
            foreach (var suppressedId in suppressedIds)
            {
                if (!Regex.IsMatch(suppressedId, "^xUnit\\d+$", RegexOptions.IgnoreCase))
                {
                    findings.Add(new WarningClassification(
                        "actionable",
                        suppressedId,
                        $"Suppression in {touchedFile}",
                        $"suppression:{touchedFile}",
                        touchedFile,
                        "Blanket xUnit suppression detected. Use explicit xUnit#### IDs only."));
                    continue;
                }

                if (!HasApprovedXunitSuppression(touchedFile, suppressedId, baseline))
                {
                    findings.Add(new WarningClassification(
                        "actionable",
                        suppressedId,
                        $"Suppression in {touchedFile}",
                        $"suppression:{touchedFile}",
                        touchedFile,
                        "xUnit suppression is not declared in scoped governance metadata."));
                }
                else
                {
                    findings.Add(new WarningClassification(
                        "known-baseline",
                        suppressedId,
                        $"Suppression in {touchedFile}",
                        $"suppression:{touchedFile}",
                        touchedFile,
                        "xUnit suppression is explicitly approved in governance metadata."));
                }
            }
        }

        return findings;
    }

    private static IEnumerable<string> ExtractSuppressedXunitIds(string content, string path)
    {
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match match in Regex.Matches(
                         content,
                         "#pragma\\s+warning\\s+disable\\s+(?<ids>.+)",
                         RegexOptions.IgnoreCase))
            {
                var idsText = match.Groups["ids"].Value;
                foreach (var token in idsText.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith("xUnit", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return token.Trim();
                    }
                }
            }

            yield break;
        }

        foreach (Match match in Regex.Matches(
                     content,
                     "<NoWarn>(?<ids>[^<]+)</NoWarn>",
                     RegexOptions.IgnoreCase))
        {
            var idsText = match.Groups["ids"].Value;
            foreach (var token in idsText.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("xUnit", StringComparison.OrdinalIgnoreCase))
                {
                    yield return token.Trim();
                }
            }
        }
    }

    private static bool HasApprovedXunitSuppression(string sourceFile, string warningId, WarningGovernanceBaseline baseline)
    {
        return baseline.XunitSuppressions.Any(x =>
            string.Equals(NormalizeRelativePath(x.Path, RootDirectory), NormalizeRelativePath(sourceFile, RootDirectory), StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.WarningId, warningId, StringComparison.OrdinalIgnoreCase)
            && !IsMissingMetadata(x.Owner, x.Rationale, x.ReviewPoint));
    }

    private static bool IsMissingMetadata(string owner, string rationale, string reviewPoint)
        => string.IsNullOrWhiteSpace(owner)
           || string.IsNullOrWhiteSpace(rationale)
           || string.IsNullOrWhiteSpace(reviewPoint);

    private static async Task<HashSet<string>> GetTouchedFilesForWarningGovernanceAsync()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gitTimeout = TimeSpan.FromSeconds(15);

        try
        {
            var statusOutput = await RunProcessAsync("git", ["status", "--porcelain"], workingDirectory: RootDirectory, timeout: gitTimeout);
            foreach (var rawLine in statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Length < 4)
                {
                    continue;
                }

                var pathPart = line[3..].Trim();
                var renameArrow = pathPart.IndexOf(" -> ", StringComparison.Ordinal);
                if (renameArrow >= 0)
                {
                    pathPart = pathPart[(renameArrow + 4)..].Trim();
                }

                if (pathPart.Length == 0)
                {
                    continue;
                }

                files.Add(NormalizeRelativePath(pathPart, RootDirectory));
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Failed to read touched files from git status: {Message}", ex.Message);
        }

        if (files.Count == 0)
        {
            try
            {
                var diffOutput = await RunProcessAsync("git", ["diff", "--name-only", "HEAD~1..HEAD"], workingDirectory: RootDirectory, timeout: gitTimeout);
                foreach (var rawLine in diffOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var path = rawLine.Trim();
                    if (path.Length == 0)
                    {
                        continue;
                    }

                    files.Add(NormalizeRelativePath(path, RootDirectory));
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("Failed to read touched files from git diff: {Message}", ex.Message);
            }
        }

        return files;
    }

    private static string? TryExtractSourceFile(string sourcePart)
    {
        var candidate = sourcePart.Trim();
        var parenIndex = candidate.LastIndexOf('(');
        if (parenIndex > 0)
        {
            candidate = candidate[..parenIndex];
        }

        if (candidate.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || candidate.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || candidate.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeRelativePath(candidate, RootDirectory);
        }

        return null;
    }

    private static string NormalizeRelativePath(string path, string rootDirectory)
    {
        var trimmed = path.Trim().Trim('"');
        var relative = Path.IsPathRooted(trimmed)
            ? Path.GetRelativePath(rootDirectory, trimmed)
            : trimmed;
        return relative.Replace('\\', '/');
    }

    // ──────────────────────────── Warning Governance Targets ────────────────────────────

    internal Target WarningGovernance => _ => _
        .Description("Classifies governed warnings and enforces warning governance gates.")
        .DependsOn(Build)
        .Executes(async () =>
        {
            await EvaluateWarningGovernanceAsync(failOnIssues: true);
        });

    internal Target WarningGovernanceSyntheticCheck => _ => _
        .Description("Runs synthetic regression checks for warning governance classifier.")
        .DependsOn(Build)
        .Executes(() =>
        {
            RunSyntheticWarningGovernanceChecks();
        });
}
