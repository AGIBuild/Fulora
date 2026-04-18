using Xunit;

// Tests run in parallel across collections (one collection per test class by default).
// Stateful tests that touch process-global resources (cwd, static fields, listeners)
// opt into the StatefulIO collection to serialize them. See StatefulIOCollection.cs.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass, MaxParallelThreads = -1)]
