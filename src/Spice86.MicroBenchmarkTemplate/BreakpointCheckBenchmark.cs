namespace Spice86.MicroBenchmarkTemplate;

using BenchmarkDotNet.Attributes;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// Benchmark comparing the cost of checking breakpoints with and without the HasActiveBreakpoints optimization.
/// This simulates the EmulationLoop hot path.
/// </summary>
[MemoryDiagnoser]
public class BreakpointCheckBenchmark {
    private BreakPointHolder _emptyHolder = null!;
    private BreakPointHolder _holderWithDisabledBreakpoint = null!;
    private BreakPointHolder _holderWithEnabledBreakpoint = null!;
    private const int Iterations = 100_000;

    [GlobalSetup]
    public void Setup() {
        // Empty holder - no breakpoints
        _emptyHolder = new BreakPointHolder();

        // Holder with a disabled breakpoint
        _holderWithDisabledBreakpoint = new BreakPointHolder();
        AddressBreakPoint disabledBreakpoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x1000,
            _ => { },
            false) {
            IsEnabled = false
        };
        _holderWithDisabledBreakpoint.ToggleBreakPoint(disabledBreakpoint, true);

        // Holder with an enabled breakpoint
        _holderWithEnabledBreakpoint = new BreakPointHolder();
        AddressBreakPoint enabledBreakpoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x1000,
            _ => { },
            false);
        _holderWithEnabledBreakpoint.ToggleBreakPoint(enabledBreakpoint, true);
    }

    [Benchmark(Baseline = true)]
    public int EmptyHolder_WithOptimization() {
        int checksPerformed = 0;
        for (int i = 0; i < Iterations; i++) {
            // This is what EmulationLoop does - check HasActiveBreakpoints first
            if (_emptyHolder.HasActiveBreakpoints) {
                checksPerformed++;
                _emptyHolder.TriggerMatchingBreakPoints(i);
            }
        }
        return checksPerformed;
    }

    [Benchmark]
    public int EmptyHolder_WithoutOptimization() {
        int checksPerformed = 0;
        for (int i = 0; i < Iterations; i++) {
            // Without optimization, we always call TriggerMatchingBreakPoints
            // This simulates the old behavior using IsEmpty
            if (!_emptyHolder.IsEmpty) {
                checksPerformed++;
                _emptyHolder.TriggerMatchingBreakPoints(i);
            }
        }
        return checksPerformed;
    }

    [Benchmark]
    public int DisabledBreakpoint_WithOptimization() {
        int checksPerformed = 0;
        for (int i = 0; i < Iterations; i++) {
            if (_holderWithDisabledBreakpoint.HasActiveBreakpoints) {
                checksPerformed++;
                _holderWithDisabledBreakpoint.TriggerMatchingBreakPoints(i);
            }
        }
        return checksPerformed;
    }

    [Benchmark]
    public int DisabledBreakpoint_WithoutOptimization() {
        int checksPerformed = 0;
        for (int i = 0; i < Iterations; i++) {
            // Without optimization, IsEmpty returns false even if breakpoint is disabled
            if (!_holderWithDisabledBreakpoint.IsEmpty) {
                checksPerformed++;
                _holderWithDisabledBreakpoint.TriggerMatchingBreakPoints(i);
            }
        }
        return checksPerformed;
    }

    [Benchmark]
    public int EnabledBreakpoint_WithOptimization() {
        int checksPerformed = 0;
        for (int i = 0; i < Iterations; i++) {
            if (_holderWithEnabledBreakpoint.HasActiveBreakpoints) {
                checksPerformed++;
                _holderWithEnabledBreakpoint.TriggerMatchingBreakPoints(i);
            }
        }
        return checksPerformed;
    }

    [Benchmark]
    public int EnabledBreakpoint_WithoutOptimization() {
        int checksPerformed = 0;
        for (int i = 0; i < Iterations; i++) {
            if (!_holderWithEnabledBreakpoint.IsEmpty) {
                checksPerformed++;
                _holderWithEnabledBreakpoint.TriggerMatchingBreakPoints(i);
            }
        }
        return checksPerformed;
    }
}