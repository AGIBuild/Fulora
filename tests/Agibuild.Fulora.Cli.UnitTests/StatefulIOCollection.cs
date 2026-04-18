using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Serializes tests that mutate process-global state and therefore cannot run
/// in parallel with anything else in the same assembly.
///
/// Members of this collection (today):
/// - <see cref="CliToolTests"/>: mutates <c>Directory.SetCurrentDirectory</c> and starts subprocesses.
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
