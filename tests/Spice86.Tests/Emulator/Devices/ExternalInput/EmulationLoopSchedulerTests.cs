namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;

using Xunit;

public sealed class EmulationLoopSchedulerTests {
    private readonly ILoggerService _logger;
    private readonly State _state;
    private readonly CyclesClock _cyclesClock;
    private readonly EmulationLoopScheduler _scheduler;

    public EmulationLoopSchedulerTests() {
        _logger = Substitute.For<ILoggerService>();
        _state = new State(CpuModel.INTEL_8086);
        // 1000 cycles = 1 second for simplicity in tests
        _cyclesClock = new CyclesClock(_state, new NullCyclesLimiter(), 1000);
        _scheduler = new EmulationLoopScheduler(_cyclesClock, _state, _logger);
    }

    [Fact]
    public void RunQueueDoesNotProcessWhenNoCyclesRemain() {
        bool invoked = false;
        // Schedule for 250ms
        // With 1000 cycles/sec, 250ms = 250 cycles
        _scheduler.AddEvent(_ => invoked = true, 250);

        _scheduler.ProcessEvents();

        invoked.Should().BeFalse();
    }

    [Fact]
    public void AddEvent_ShouldExecuteAfterDelay() {
        bool invoked = false;

        // Schedule event after 100ms
        _scheduler.AddEvent(_ => invoked = true, 100);

        // Advance time by 50ms (50 cycles)
        AdvanceCycles(50);
        _scheduler.ProcessEvents();
        invoked.Should().BeFalse();

        // Advance time by another 50ms (total 100 cycles)
        AdvanceCycles(50);
        _scheduler.ProcessEvents();
        invoked.Should().BeTrue();
    }

    [Fact]
    public void ProcessEvents_ShouldExecuteMultipleEventsInOrder() {
        var executionOrder = new List<int>();

        _scheduler.AddEvent(_ => executionOrder.Add(2), 200);
        _scheduler.AddEvent(_ => executionOrder.Add(1), 100);
        _scheduler.AddEvent(_ => executionOrder.Add(3), 300);

        AdvanceCycles(300);
        _scheduler.ProcessEvents();

        executionOrder.Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public void ProcessEvents_ShouldHandleEmptyQueue() {
        // Should not throw
        _scheduler.ProcessEvents();
    }

    [Fact]
    public void RemoveEvents_ShouldCancelEvents() {
        bool invoked = false;
        EventHandler handler = _ => invoked = true;

        _scheduler.AddEvent(handler, 100);
        _scheduler.RemoveEvents(handler);

        AdvanceCycles(100);
        _scheduler.ProcessEvents();

        invoked.Should().BeFalse();
    }

    [Fact]
    public void RemoveEvents_ShouldOnlyRemoveSpecifiedHandler() {
        bool invoked1 = false;
        bool invoked2 = false;
        EventHandler handler1 = _ => invoked1 = true;
        EventHandler handler2 = _ => invoked2 = true;

        _scheduler.AddEvent(handler1, 100);
        _scheduler.AddEvent(handler2, 100);

        _scheduler.RemoveEvents(handler1);

        AdvanceCycles(100);
        _scheduler.ProcessEvents();

        invoked1.Should().BeFalse();
        invoked2.Should().BeTrue();
    }

    [Fact]
    public void AddEvent_WithZeroDelay_ShouldExecuteImmediately() {
        bool invoked = false;

        _scheduler.AddEvent(_ => invoked = true, 0);
        _scheduler.ProcessEvents();

        invoked.Should().BeTrue();
    }

    [Fact]
    public void AddEvent_WithPastTime_ShouldExecuteImmediately() {
        bool invoked = false;

        // Add event with negative delay (past time)
        _scheduler.AddEvent(_ => invoked = true, -100);
        _scheduler.ProcessEvents();

        invoked.Should().BeTrue();
    }

    [Fact]
    public void AddEvent_WithClockAhead_ShouldExecuteImmediately() {
        bool invoked = false;

        // Advance time by 100ms (100 cycles)
        AdvanceCycles(100);

        // Add event with 50ms delay (scheduled time = 150ms)
        // But wait, if we add event with 50ms delay, it is scheduled at CurrentTime + 50.
        // The user asked: "Add a test with a non negative time event and a clock that is ahead of that when event is processed."
        // This implies:
        // 1. Add event scheduled for T=100.
        // 2. Advance clock to T=150.
        // 3. Process events.

        // Add event with 100ms delay (scheduled at 100ms since clock starts at 0)
        _scheduler.AddEvent(_ => invoked = true, 100);

        // Advance clock to 150ms
        AdvanceCycles(150);

        _scheduler.ProcessEvents();

        invoked.Should().BeTrue();
    }

    [Fact]
    public void AddEvent_WhenQueueFull_ShouldIgnoreEventButExecuteExisting() {
        int maxQueueSize = 8192; // Assuming this is the constant in EmulationLoopScheduler
        int eventsCount = maxQueueSize + 10;
        var executedEvents = new List<int>();

        // Fill the queue
        for (int i = 0; i < maxQueueSize; i++) {
            int val = i;
            _scheduler.AddEvent(_ => executedEvents.Add(val), 100 + i);
        }

        // Try to add more events (should be ignored)
        for (int i = maxQueueSize; i < eventsCount; i++) {
            int val = i;
            _scheduler.AddEvent(_ => executedEvents.Add(val), 100 + i);
        }

        // Advance time enough to process all potential events
        AdvanceCycles(100 + eventsCount);
        _scheduler.ProcessEvents();

        // Verify that only the first maxQueueSize events were executed
        executedEvents.Count.Should().Be(maxQueueSize);
        executedEvents.Should().Contain(Enumerable.Range(0, maxQueueSize));
        executedEvents.Should().NotContain(Enumerable.Range(maxQueueSize, 10));
    }

    private void AdvanceCycles(long cycles) {
        for (int i = 0; i < cycles; i++) {
            _state.IncCycles();
        }
    }
}
