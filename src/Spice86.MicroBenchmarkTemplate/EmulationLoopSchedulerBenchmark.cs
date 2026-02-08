namespace Spice86.MicroBenchmarkTemplate;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;

/// <summary>
/// Benchmark for EmulationLoopScheduler.ProcessEvents() hot path.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class EmulationLoopSchedulerBenchmark {
    private State _state = null!;
    private CpuCycleLimiter _limiter = null!;
    private EmulatedClock _clock = null!;
    private EmulationLoopScheduler _scheduler = null!;
    private ILoggerService _logger = null!;

    private const int CyclesPerMs = 3000;

    [GlobalSetup]
    public void Setup() {
        _logger = Substitute.For<ILoggerService>();
        _state = new State(CpuModel.INTEL_80386);
        _limiter = new CpuCycleLimiter(_state, CyclesPerMs);
        _clock = new EmulatedClock(_limiter);
        _scheduler = new EmulationLoopScheduler(_clock, _state, _logger);
    }

    [Benchmark(Baseline = true)]
    public void EmptyQueue_NoTick() {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, CyclesPerMs);
        var clock = new EmulatedClock(limiter);
        var scheduler = new EmulationLoopScheduler(clock, state, _logger);

        for (int i = 0; i < CyclesPerMs - 1; i++) {
            state.IncCycles();
            scheduler.ProcessEvents();
        }
    }

    [Benchmark]
    public void WithTickBoundary() {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, CyclesPerMs);
        var clock = new EmulatedClock(limiter);
        var scheduler = new EmulationLoopScheduler(clock, state, _logger);

        for (int i = 0; i < CyclesPerMs * 5; i++) {
            state.IncCycles();
            limiter.RegulateCycles();
            scheduler.ProcessEvents();
        }
    }
}
