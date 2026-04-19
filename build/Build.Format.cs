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
            // dotnet format internally evaluates each csproj with MSBuild. When the Android
            // workload is missing on this host, Platforms.csproj's net10.0-android slice
            // would fail at NETSDK1147 during workspace load. MSBuild auto-imports environment
            // variables as properties, so exporting EnableAndroidTfm=false drops the android
            // slice for the format-check workspace evaluation.
            if (!await HasDotNetWorkloadAsync("android") || !HasAndroidSdkInstalled())
            {
                Environment.SetEnvironmentVariable("EnableAndroidTfm", "false");
                Serilog.Log.Information("Android workload not installed — exporting EnableAndroidTfm=false for dotnet format.");
            }

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
