namespace Spice86.MicroBenchmarkTemplate;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;

using System.Runtime.CompilerServices;

/// <summary>
/// Benchmark for CpuCycleLimiter.RegulateCycles() hot path.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class CyclesLimiterBenchmark {
    private State _state = null!;
    private CpuCycleLimiter _limiter = null!;
    
    private const int CyclesPerMs = 3000;

    [GlobalSetup]
    public void Setup() {
        _state = new State(CpuModel.INTEL_80386);
        _limiter = new CpuCycleLimiter(_state, CyclesPerMs);
    }

    [Benchmark(Baseline = true)]
    public void FastPath_NoTickBoundary() {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, CyclesPerMs);
        
        for (int i = 0; i < CyclesPerMs - 1; i++) {
            state.IncCycles();
            limiter.RegulateCycles();
        }
    }

    [Benchmark]
    public void Baseline_NoLimiter() {
        for (int i = 0; i < CyclesPerMs - 1; i++) {
            _state.IncCycles();
        }
    }

    [Benchmark]
    public int TickBoundaryCrossing() {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, CyclesPerMs);
        
        int tickCount = 0;
        for (int i = 0; i < CyclesPerMs * 10; i++) {
            state.IncCycles();
            limiter.RegulateCycles();
            if (limiter.TickOccurred) {
                tickCount++;
            }
        }
        return tickCount;
    }

    [Benchmark]
    public bool TickOccurredCheck() {
        bool result = false;
        for (int i = 0; i < 1_000_000; i++) {
            result = _limiter.TickOccurred;
        }
        return result;
    }
}
