using Xunit;

// Tests run in parallel across collections (one collection per test class by default).
// CliToolTests mutates Directory.SetCurrentDirectory and starts subprocesses, so it
// opts into the StatefulIO collection to serialize itself. See StatefulIOCollection.cs.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass, MaxParallelThreads = -1)]
