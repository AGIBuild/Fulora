using System;
using Nuke.Common;
using Nuke.Common.Tooling;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal partial class BuildTask
{
    internal Target Format => _ => _
        .Description("Verifies that code formatting matches .editorconfig rules. Fails if any files would be changed.")
        .DependsOn(Restore)
        .Executes(async () =>
        {
            var filterPath = await BuildPlatformAwareSolutionFilterAsync("format-check");
            DotNet($"format {filterPath} --verify-no-changes",
                   workingDirectory: RootDirectory,
                   logger: (type, message) =>
                   {
                       if (type == OutputType.Err
                           && message.Contains("Warnings were encountered while loading the workspace", StringComparison.Ordinal))
                       {
                           Serilog.Log.Warning(message);
                           return;
                       }

                       ProcessTasks.DefaultLogger(type, message);
                   });
        });
}
