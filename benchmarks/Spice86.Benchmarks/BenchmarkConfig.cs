namespace Spice86.Benchmarks;
using System.Collections.Generic;
using System.Linq;

using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Diagnostics.Windows;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

internal class BenchmarkConfig
{
    /// <summary>
    /// Get a custom configuration
    /// </summary>
    /// <returns></returns>
    public static IConfig Get()
    {
        return ManualConfig.CreateEmpty()
            // Jobs
            .AddJob(Job.Default
                .WithRuntime(CoreRuntime.Core80)
                .WithPlatform(Platform.X64))
            // Configuration of diagnosers and outputs
            .AddDiagnoser(MemoryDiagnoser.Default, ThreadingDiagnoser.Default, new InliningDiagnoser())
            .AddColumnProvider(DefaultColumnProviders.Instance)
            .AddLogger(ConsoleLogger.Default)
            .AddExporter(CsvExporter.Default)
            .AddAnalyser(GetAnalysers().ToArray());
    }

    /// <summary>
    /// Get analyser for the cutom configuration
    /// </summary>
    /// <returns></returns>
    private static IEnumerable<IAnalyser> GetAnalysers()
    {
        yield return EnvironmentAnalyser.Default;
        yield return OutliersAnalyser.Default;
        yield return MinIterationTimeAnalyser.Default;
        yield return MultimodalDistributionAnalyzer.Default;
        yield return RuntimeErrorAnalyser.Default;
        yield return ZeroMeasurementAnalyser.Default;
        yield return BaselineCustomAnalyzer.Default;
    }
}
