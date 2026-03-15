using System;
using System.IO;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    internal Target OpenSpecStrictGovernance => _ => _
        .Description("Runs OpenSpec strict validation as a hard governance gate.")
        .Executes(async () =>
        {
            TestResultsDirectory.CreateDirectory();
            var governanceTimeout = TimeSpan.FromMinutes(3);
            var output = OperatingSystem.IsWindows()
                ? await RunProcessCheckedAsync(
                    "powershell",
                    ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "npm exec --yes @fission-ai/openspec -- validate --all --strict"],
                    workingDirectory: RootDirectory,
                    timeout: governanceTimeout)
                : await RunProcessCheckedAsync(
                    "bash",
                    ["-lc", "npm exec --yes @fission-ai/openspec -- validate --all --strict"],
                    workingDirectory: RootDirectory,
                    timeout: governanceTimeout);
            File.WriteAllText(OpenSpecStrictGovernanceReportFile, output);
            Serilog.Log.Information("OpenSpec strict governance report written to {Path}", OpenSpecStrictGovernanceReportFile);
        });
}
