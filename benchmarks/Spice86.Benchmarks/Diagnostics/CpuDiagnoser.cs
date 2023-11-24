namespace Spice86.Benchmarks.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

internal class CpuDiagnoser : IDiagnoser
{
    public IEnumerable<string> Ids { get; }
    public IEnumerable<IExporter> Exporters { get; }
    public IEnumerable<IAnalyser> Analysers { get; }

    public void DisplayResults(ILogger logger)
    {
        throw new NotImplementedException();
    }

    public RunMode GetRunMode(BenchmarkCase benchmarkCase)
    {
        throw new NotImplementedException();
    }

    public void Handle(HostSignal signal, DiagnoserActionParameters parameters)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<Metric> ProcessResults(DiagnoserResults results)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
    {
        throw new NotImplementedException();
    }
}
