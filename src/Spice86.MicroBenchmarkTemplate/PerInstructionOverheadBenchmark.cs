namespace Spice86.MicroBenchmarkTemplate;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

/// <summary>
/// Per-instruction overhead benchmarks for the emulation loop.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class PerInstructionOverheadBenchmark {
    private State _state = null!;
    private CpuCycleLimiter _limiter = null!;
    private ILoggerService _logger = null!;

    private const int Instructions = 1_000_000;
    private const int CyclesPerMs = 3000;

    [GlobalSetup]
    public void Setup() {
        _logger = Substitute.For<ILoggerService>();
        _state = new State(CpuModel.INTEL_80386);
        _limiter = new CpuCycleLimiter(_state, CyclesPerMs);
    }

    [Benchmark(Baseline = true)]
    public int EmptyLoop() {
        int dummy = 0;
        for (int i = 0; i < Instructions; i++) {
            dummy++;
        }
        return dummy;
    }

    [Benchmark]
    public long JustIncCycles() {
        var state = new State(CpuModel.INTEL_80386);
        for (int i = 0; i < Instructions; i++) {
            state.IncCycles();
        }
        return state.Cycles;
    }

    [Benchmark]
    public long RegulateCyclesFastPathOnly() {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, int.MaxValue);
        
        for (int i = 0; i < Instructions; i++) {
            limiter.RegulateCycles();
        }
        return state.Cycles;
    }

    [Benchmark]
    public long RegulateCyclesMixed() {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, CyclesPerMs);
        
        for (int i = 0; i < Instructions; i++) {
            state.IncCycles();
            limiter.RegulateCycles();
        }
        return state.Cycles;
    }

    [Benchmark]
    public long ProcessEventsFastPathOnly() {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, int.MaxValue);
        var clock = new EmulatedClock(limiter);
        var scheduler = new EmulationLoopScheduler(clock, state, _logger);
        
        for (int i = 0; i < Instructions; i++) {
            scheduler.ProcessEvents();
        }
        return state.Cycles;
    }

    [Benchmark]
    public long FullLoopOverhead() {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, CyclesPerMs);
        var clock = new EmulatedClock(limiter);
        var scheduler = new EmulationLoopScheduler(clock, state, _logger);
        
        while (state.Cycles < Instructions) {
            state.IncCycles();
            scheduler.ProcessEvents();
            limiter.RegulateCycles();
        }
        return state.Cycles;
    }
}
