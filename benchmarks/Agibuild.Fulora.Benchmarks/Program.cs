using Agibuild.Fulora.Benchmarks;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(BridgeBenchmarks).Assembly).Run(args);
