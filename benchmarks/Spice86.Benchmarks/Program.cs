// If arguments are available use BenchmarkSwitcher to run benchmarks
using BenchmarkDotNet.Running;

using Spice86.Benchmarks;

if (args.Length > 0)
{
    var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
        .Run(args, BenchmarkConfig.Get());
    return;
}
// Else, use BenchmarkRunner
var summary = BenchmarkRunner.Run<Benchmarks>(BenchmarkConfig.Get());