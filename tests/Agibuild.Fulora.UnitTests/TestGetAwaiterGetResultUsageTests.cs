using System.Text;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class TestGetAwaiterGetResultUsageTests
{
    [Fact]
    public void Test_sources_limit_GetAwaiterGetResult_to_approved_threading_boundaries()
    {
        var repoRoot = FindRepoRoot();
        var testsRoot = Path.Combine(repoRoot, "tests");
        var files = Directory.GetFiles(testsRoot, "*.cs", SearchOption.AllDirectories);

        var approvedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "tests/Agibuild.Fulora.Testing/DispatcherTestPump.cs",
            "tests/Agibuild.Fulora.Testing/ThreadingTestHelper.cs",
            "tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1AnyThreadAsyncApiTests.cs",
            "tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1BaseUrlTests.cs",
            "tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1EventThreadingTests.cs",
            "tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1NativeNavigationTests.cs",
            "tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1OperationQueueTests.cs",
            "tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1SourceAndStopTests.cs",
            "tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1ThreadingTests.cs",
            "tests/Agibuild.Fulora.UnitTests/CoverageGapTests.cs",
            "tests/Agibuild.Fulora.UnitTests/WebViewCoreCoverageTests.cs",
            "tests/Agibuild.Fulora.UnitTests/WebDialogCoverageTests.cs",
            "tests/Agibuild.Fulora.UnitTests/WebDialogTests.cs",
            "tests/Agibuild.Fulora.Integration.Tests.Automation/WebAuthBrokerIntegrationTests.cs",
            "tests/Agibuild.Fulora.UnitTests/Plugins/HttpClientServiceTests.cs",
            "tests/Agibuild.Fulora.UnitTests/BridgeDebugServerTests.cs",
        };

        var found = new List<(string File, string Line)>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            if (relative.EndsWith("GetAwaiterGetResultUsageTests.cs", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (!line.Contains(".GetAwaiter().GetResult()", StringComparison.Ordinal))
                {
                    continue;
                }

                found.Add((relative, line.Trim()));
            }
        }

        Assert.NotEmpty(found);
        foreach (var (file, line) in found)
        {
            if (!approvedFiles.Contains(file))
            {
                Assert.Fail($"Unexpected test-side GetAwaiter().GetResult() in {file}: {line}");
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Agibuild.Fulora.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
