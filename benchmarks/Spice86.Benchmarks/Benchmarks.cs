namespace Spice86.Benchmarks;

using BenchmarkDotNet.Attributes;

using Spice86.Core.Emulator;
using Spice86.Logging;

public class Benchmarks
{
    [Benchmark]
    public void CpuPerformance()
    {

        var thread = new Thread(static () =>
        {
            using var program = new ProgramExecutor(new Core.CLI.Configuration()
            {
                DumpDataOnExit = true,
                Exe = @"C:\Jeux\ABWFR\DUNE_CDVF\C\DNCDPRG.EXE"
            }, new LoggerService(new LoggerPropertyBag()), default);
            program.Run(400000);
        });
        thread.Start();
        thread.Join();
    }
}