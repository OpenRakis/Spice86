namespace Spice86.Tests;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;

/// <summary>
/// Simple performance test to demonstrate that HasActiveBreakpoints avoids unnecessary work.
/// This is not a BenchmarkDotNet benchmark, but a simple test showing the performance difference.
/// </summary>
public class BreakpointPerformanceBenchmark {
    [Fact]
    public void HasActiveBreakpointsAvoidsUnnecessaryChecks() {
        BreakPointHolder holder = new();
        
        // Simulate the common case: no breakpoints active
        // In the old code (using IsEmpty), we would always check even with no breakpoints
        // In the new code (using HasActiveBreakpoints), we can skip the check
        
        const int iterations = 1_000_000;
        int checksPerformed = 0;
        
        // Measure with no breakpoints - this simulates the hot path in EmulationLoop
        for (int i = 0; i < iterations; i++) {
            if (holder.HasActiveBreakpoints) {
                checksPerformed++;
                holder.TriggerMatchingBreakPoints(i);
            }
        }
        
        // Should not have performed any checks since there are no active breakpoints
        Assert.Equal(0, checksPerformed);
        
        // Now add a disabled breakpoint
        AddressBreakPoint breakPoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x1000,
            _ => { },
            false) {
            IsEnabled = false
        };
        holder.ToggleBreakPoint(breakPoint, true);
        
        checksPerformed = 0;
        for (int i = 0; i < iterations; i++) {
            if (holder.HasActiveBreakpoints) {
                checksPerformed++;
                holder.TriggerMatchingBreakPoints(i);
            }
        }
        
        // Should still not have performed any checks since the breakpoint is disabled
        Assert.Equal(0, checksPerformed);
        
        // Now enable the breakpoint
        breakPoint.IsEnabled = true;
        
        checksPerformed = 0;
        for (int i = 0; i < iterations; i++) {
            if (holder.HasActiveBreakpoints) {
                checksPerformed++;
                holder.TriggerMatchingBreakPoints(i);
            }
        }
        
        // Should have performed checks on all iterations
        Assert.Equal(iterations, checksPerformed);
    }
}
