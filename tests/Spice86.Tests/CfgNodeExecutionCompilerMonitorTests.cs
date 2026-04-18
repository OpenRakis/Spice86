namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Shared.Interfaces;

using Xunit;

public class CfgNodeExecutionCompilerMonitorTests {
    private static CfgNodeExecutionCompilerMonitor CreateMonitor() {
        // Create the monitor and immediately dispose its timer so background logging
        // won't interfere with state assertions during tests.
        ILoggerService logger = Substitute.For<ILoggerService>();
        CfgNodeExecutionCompilerMonitor monitor = new CfgNodeExecutionCompilerMonitor(logger);
        monitor.Dispose();
        return monitor;
    }

    [Fact]
    public void InitialState_AllCountersAreZero() {
        CfgNodeExecutionCompilerMonitor monitor = CreateMonitor();

        (monitor.TotalInterpreted - monitor.TotalSwapped).Should().Be(0);
        monitor.QueueDepth.Should().Be(0);
        monitor.TotalInterpreted.Should().Be(0);
        monitor.TotalSwapped.Should().Be(0);
        monitor.TotalSuccess.Should().Be(0);
        monitor.TotalFailures.Should().Be(0);
    }

    [Fact]
    public void RecordInterpreted_IncrementsPendingCount() {
        CfgNodeExecutionCompilerMonitor monitor = CreateMonitor();

        monitor.RecordInterpreted();

        monitor.TotalInterpreted.Should().Be(1);
        (monitor.TotalInterpreted - monitor.TotalSwapped).Should().Be(1);
    }

    [Fact]
    public void RecordSwapped_DecrementsPendingCount() {
        CfgNodeExecutionCompilerMonitor monitor = CreateMonitor();

        monitor.RecordInterpreted();
        monitor.RecordSwapped();

        monitor.TotalSwapped.Should().Be(1);
        (monitor.TotalInterpreted - monitor.TotalSwapped).Should().Be(0);
    }

    [Fact]
    public void FullCompileLifecycle_PendingAndQueueReturnToZero() {
        CfgNodeExecutionCompilerMonitor monitor = CreateMonitor();

        // Simulate CfgNodeExecutionCompiler.Compile():
        monitor.RecordInterpreted();   // interpreted delegate assigned
        monitor.RecordQueuePushed();   // item pushed to channel

        // Simulate background thread processing:
        monitor.RecordCompileSuccess(500);  // compilation succeeded
        monitor.RecordQueuePopped();        // item removed from channel (finally block)

        // Simulate ContinueWith callback:
        monitor.RecordSwapped();            // optimized delegate swapped in

        (monitor.TotalInterpreted - monitor.TotalSwapped).Should().Be(0);
        monitor.QueueDepth.Should().Be(0);
        monitor.TotalSuccess.Should().Be(1);
        monitor.TotalInterpreted.Should().Be(1);
        monitor.TotalSwapped.Should().Be(1);
    }

    [Fact]
    public void RecordQueuePushed_IncrementsQueueDepth() {
        CfgNodeExecutionCompilerMonitor monitor = CreateMonitor();

        monitor.RecordQueuePushed();

        monitor.QueueDepth.Should().Be(1);
    }

    [Fact]
    public void RecordQueuePopped_DecrementsQueueDepth() {
        CfgNodeExecutionCompilerMonitor monitor = CreateMonitor();

        monitor.RecordQueuePushed();
        monitor.RecordQueuePopped();

        monitor.QueueDepth.Should().Be(0);
    }

    [Fact]
    public void MultipleNodes_PendingCountTracksAllNodes() {
        CfgNodeExecutionCompilerMonitor monitor = CreateMonitor();

        // Three nodes enqueued for compilation.
        monitor.RecordInterpreted();
        monitor.RecordQueuePushed();
        monitor.RecordInterpreted();
        monitor.RecordQueuePushed();
        monitor.RecordInterpreted();
        monitor.RecordQueuePushed();

        (monitor.TotalInterpreted - monitor.TotalSwapped).Should().Be(3);
        monitor.QueueDepth.Should().Be(3);

        // Two nodes compiled and swapped.
        monitor.RecordCompileSuccess(100);
        monitor.RecordQueuePopped();
        monitor.RecordSwapped();

        monitor.RecordCompileSuccess(200);
        monitor.RecordQueuePopped();
        monitor.RecordSwapped();

        (monitor.TotalInterpreted - monitor.TotalSwapped).Should().Be(1);
        monitor.QueueDepth.Should().Be(1);

        // Third node compiled and swapped.
        monitor.RecordCompileSuccess(150);
        monitor.RecordQueuePopped();
        monitor.RecordSwapped();

        (monitor.TotalInterpreted - monitor.TotalSwapped).Should().Be(0);
        monitor.QueueDepth.Should().Be(0);
    }

    [Fact]
    public void RecordCompileSuccess_IncrementsTotalSuccess() {
        CfgNodeExecutionCompilerMonitor monitor = CreateMonitor();

        monitor.RecordInterpreted();
        monitor.RecordQueuePushed();
        monitor.RecordCompileSuccess(1000);
        monitor.RecordQueuePopped();
        monitor.RecordSwapped();

        monitor.TotalSuccess.Should().Be(1);
        monitor.TotalFailures.Should().Be(0);
    }

    [Fact]
    public void RecordCompileFailure_IncrementsTotalFailures() {
        CfgNodeExecutionCompilerMonitor monitor = CreateMonitor();

        monitor.RecordInterpreted();
        monitor.RecordQueuePushed();
        monitor.RecordCompileFailure(300);
        monitor.RecordQueuePopped();
        // On failure the ContinueWith skips RecordSwapped, so pending stays 1.

        monitor.TotalFailures.Should().Be(1);
        monitor.TotalSuccess.Should().Be(0);
        monitor.QueueDepth.Should().Be(0);
        (monitor.TotalInterpreted - monitor.TotalSwapped).Should().Be(1);
    }
}
