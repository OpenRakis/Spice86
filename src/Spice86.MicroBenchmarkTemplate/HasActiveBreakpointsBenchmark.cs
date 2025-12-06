namespace Spice86.MicroBenchmarkTemplate;

using BenchmarkDotNet.Attributes;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// Micro-benchmark to compare the performance of HasActiveBreakpoints property access
/// between dynamic iteration (current) and event-based counter (proposed) approaches.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class HasActiveBreakpointsBenchmark {
    private BreakPointHolder _emptyHolder = null!;
    private BreakPointHolder _holderWithOneDisabledBreakpoint = null!;
    private BreakPointHolder _holderWithFiveDisabledBreakpoints = null!;
    private BreakPointHolder _holderWithTenDisabledBreakpoints = null!;
    private BreakPointHolder _holderWithOneEnabledBreakpoint = null!;
    private BreakPointHolder _holderWithMixedBreakpoints = null!;

    [GlobalSetup]
    public void Setup() {
        // Empty holder
        _emptyHolder = new BreakPointHolder();

        // Holder with one disabled breakpoint
        _holderWithOneDisabledBreakpoint = new BreakPointHolder();
        AddressBreakPoint bp1 = new(BreakPointType.CPU_EXECUTION_ADDRESS, 0x1000, _ => { }, false) { IsEnabled = false };
        _holderWithOneDisabledBreakpoint.ToggleBreakPoint(bp1, true);

        // Holder with five disabled breakpoints
        _holderWithFiveDisabledBreakpoints = new BreakPointHolder();
        for (int i = 0; i < 5; i++) {
            AddressBreakPoint bp = new(BreakPointType.CPU_EXECUTION_ADDRESS, 0x1000 + i, _ => { }, false) { IsEnabled = false };
            _holderWithFiveDisabledBreakpoints.ToggleBreakPoint(bp, true);
        }

        // Holder with ten disabled breakpoints
        _holderWithTenDisabledBreakpoints = new BreakPointHolder();
        for (int i = 0; i < 10; i++) {
            AddressBreakPoint bp = new(BreakPointType.CPU_EXECUTION_ADDRESS, 0x1000 + i, _ => { }, false) { IsEnabled = false };
            _holderWithTenDisabledBreakpoints.ToggleBreakPoint(bp, true);
        }

        // Holder with one enabled breakpoint (at the end after disabled ones)
        _holderWithOneEnabledBreakpoint = new BreakPointHolder();
        AddressBreakPoint enabled = new(BreakPointType.CPU_EXECUTION_ADDRESS, 0x1000, _ => { }, false);
        _holderWithOneEnabledBreakpoint.ToggleBreakPoint(enabled, true);

        // Holder with mixed breakpoints (worst case - enabled is last)
        _holderWithMixedBreakpoints = new BreakPointHolder();
        for (int i = 0; i < 9; i++) {
            AddressBreakPoint bp = new(BreakPointType.CPU_EXECUTION_ADDRESS, 0x1000 + i, _ => { }, false) { IsEnabled = false };
            _holderWithMixedBreakpoints.ToggleBreakPoint(bp, true);
        }
        AddressBreakPoint enabledLast = new(BreakPointType.CPU_EXECUTION_ADDRESS, 0x2000, _ => { }, false);
        _holderWithMixedBreakpoints.ToggleBreakPoint(enabledLast, true);
    }

    [Benchmark(Baseline = true)]
    public bool EmptyHolder() {
        return _emptyHolder.HasActiveBreakpoints;
    }

    [Benchmark]
    public bool OneDisabledBreakpoint() {
        return _holderWithOneDisabledBreakpoint.HasActiveBreakpoints;
    }

    [Benchmark]
    public bool FiveDisabledBreakpoints() {
        return _holderWithFiveDisabledBreakpoints.HasActiveBreakpoints;
    }

    [Benchmark]
    public bool TenDisabledBreakpoints() {
        return _holderWithTenDisabledBreakpoints.HasActiveBreakpoints;
    }

    [Benchmark]
    public bool OneEnabledBreakpoint() {
        return _holderWithOneEnabledBreakpoint.HasActiveBreakpoints;
    }

    [Benchmark]
    public bool MixedBreakpointsEnabledLast() {
        return _holderWithMixedBreakpoints.HasActiveBreakpoints;
    }
}