namespace Agibuild.Fulora.Cli.Commands;

internal sealed record PackageProfile(string Name, string Channel, string? Runtime, bool Notarize);

internal static class PackageProfileDefaults
{
    private static readonly IReadOnlyDictionary<string, PackageProfile> Profiles =
        new Dictionary<string, PackageProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["desktop-internal"] = new("desktop-internal", "internal", null, false),
            ["desktop-public"] = new("desktop-public", "stable", null, false),
            ["mac-notarized"] = new("mac-notarized", "stable", "osx-arm64", true),
        };

    public static PackageProfile Resolve(string name)
    {
        if (TryResolve(name, out var profile))
        {
            return profile;
        }

        throw new ArgumentException(
            $"Unknown package profile '{name}'. Supported profiles: {string.Join(", ", Profiles.Keys)}.",
            nameof(name));
    }

    public static bool TryResolve(string? name, out PackageProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(name) && Profiles.TryGetValue(name.Trim(), out profile!))
        {
            return true;
        }

        profile = new PackageProfile(string.Empty, "stable", null, false);
        return false;
    }
}
