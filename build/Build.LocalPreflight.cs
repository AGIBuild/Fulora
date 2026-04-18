using Nuke.Common;

internal partial class BuildTask
{
    internal Target LocalPreflight => _ => _
        .Description(
            "One-command pre-commit gate: dotnet format --verify-no-changes -> "
          + "platform-aware Build (skips unavailable workloads automatically) -> "
          + "UnitTests (all non-platform unit test projects). "
          + "Aim: < 2 min on a warm cache. Run this before every push.")
        .DependsOn(Format, UnitTests);
}
