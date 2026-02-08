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
/// GC and allocation benchmarks for the emulation loop.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[GcForce(true)]
public class EmulationLoopGCBenchmark {
    private State _state = null!;
    private CpuCycleLimiter _limiter = null!;
    private EmulatedClock _clock = null!;
    private EmulationLoopScheduler _scheduler = null!;
    private ILoggerService _logger = null!;

    private const int CyclesPerMs = 3000;
    private const int TotalCycles = 100_000;

    [GlobalSetup]
    public void Setup() {
        _logger = Substitute.For<ILoggerService>();
        _state = new State(CpuModel.INTEL_80386);
        _limiter = new CpuCycleLimiter(_state, CyclesPerMs);
        _clock = new EmulatedClock(_limiter);
        _scheduler = new EmulationLoopScheduler(_clock, _state, _logger);
    }

    [Benchmark(Baseline = true)]
    public long JustIncCycles() {
        var state = new State(CpuModel.INTEL_80386);
        for (int i = 0; i < TotalCycles; i++) {
            state.IncCycles();
        }
        return state.Cycles;
    }

    [Benchmark]
    public long WithScheduler() {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, CyclesPerMs);
        var clock = new EmulatedClock(limiter);
        var scheduler = new EmulationLoopScheduler(clock, state, _logger);
        
        for (int i = 0; i < TotalCycles; i++) {
            state.IncCycles();
            limiter.RegulateCycles();
            scheduler.ProcessEvents();
        }
        return state.Cycles;
    }
}
