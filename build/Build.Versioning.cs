using System;
using System.IO;
using System.Xml.Linq;
using Nuke.Common;

internal partial class BuildTask
{
    [Parameter("Target version to set (X.Y.Z format). If omitted, patch is auto-incremented.")]
    private readonly string? UpdateVersionTo = null;

    internal Target ShowVersion => _ => _
        .Description("Displays current version information from Directory.Build.props.")
        .Executes(() =>
        {
            var doc = XDocument.Load(RootDirectory / "Directory.Build.props");
            var versionPrefix = doc.Descendants("VersionPrefix").FirstOrDefault()?.Value.Trim()
                ?? "(not found)";

            var fullVersion = string.IsNullOrEmpty(VersionSuffix)
                ? versionPrefix
                : $"{versionPrefix}-{VersionSuffix}";

            Serilog.Log.Information("VersionPrefix : {Version}", versionPrefix);
            Serilog.Log.Information("VersionSuffix : {Suffix}", VersionSuffix ?? "(none)");
            Serilog.Log.Information("Full Version  : {FullVersion}", fullVersion);
        });

    internal Target UpdateVersion => _ => _
        .Description("Updates the VersionPrefix in Directory.Build.props. Auto-increments patch if no version is specified; validates new > current when specified.")
        .Executes(() =>
        {
            var propsPath = RootDirectory / "Directory.Build.props";
            var doc = XDocument.Load(propsPath);
            var versionElement = doc.Descendants("VersionPrefix").FirstOrDefault()
                ?? throw new InvalidOperationException("No <VersionPrefix> element found in Directory.Build.props");

            var currentVersionStr = versionElement.Value.Trim();
            if (!Version.TryParse(currentVersionStr, out var currentVersion) || currentVersion.Major < 0)
                throw new InvalidOperationException($"Current VersionPrefix '{currentVersionStr}' is not a valid X.Y.Z version.");

            var currentNormalized = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(currentVersion.Build, 0));

            Version newVersion;
            if (string.IsNullOrWhiteSpace(UpdateVersionTo))
            {
                newVersion = new Version(currentNormalized.Major, currentNormalized.Minor, currentNormalized.Build + 1);
                Serilog.Log.Information("Auto-incrementing patch: {Current} → {New}", currentNormalized, newVersion);
            }
            else
            {
                if (!Version.TryParse(UpdateVersionTo, out var parsed) || parsed.Major < 0)
                    throw new InvalidOperationException($"Specified version '{UpdateVersionTo}' is not a valid X.Y.Z format.");

                newVersion = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));

                if (newVersion <= currentNormalized)
                    throw new InvalidOperationException(
                        $"Specified version {newVersion} must be strictly greater than current version {currentNormalized}.");

                Serilog.Log.Information("Explicit version update: {Current} → {New}", currentNormalized, newVersion);
            }

            var newVersionStr = $"{newVersion.Major}.{newVersion.Minor}.{newVersion.Build}";

            var content = File.ReadAllText(propsPath);
            var updated = content.Replace(
                $"<VersionPrefix>{currentVersionStr}</VersionPrefix>",
                $"<VersionPrefix>{newVersionStr}</VersionPrefix>");

            if (string.Equals(content, updated, StringComparison.Ordinal))
                throw new InvalidOperationException($"Failed to locate <VersionPrefix>{currentVersionStr}</VersionPrefix> for replacement.");

            File.WriteAllText(propsPath, updated);

            Serilog.Log.Information("VersionPrefix updated to {Version} in Directory.Build.props", newVersionStr);
        });
}
