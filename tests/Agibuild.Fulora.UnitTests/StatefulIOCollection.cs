using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Serializes tests that mutate process-global state and therefore cannot run
/// in parallel with anything else in the same assembly.
///
/// Members of this collection (today):
/// - <see cref="BridgeDebugServerTests"/>: shares a static <c>CancellationTokenSource</c>.
/// - <see cref="CliToolTests"/>: mutates <c>Directory.SetCurrentDirectory</c> and starts subprocesses.
/// - <see cref="RuntimeCoverageTests"/> (partial across 4 files): opens <c>HttpListener</c>/<c>TcpListener</c>
///   instances; even with port 0, in-process listener cleanup races at high
///   parallelism led to flaky teardown in earlier experiments.
///
/// Add new types here only when truly necessary; prefer making tests independent
/// (per-test temp directories, dynamic ports, no static fields) over enrolling
/// them in this serialized lane.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StatefulIOCollection
{
    public const string Name = "StatefulIO";
}
