namespace Spice86.Tests;

using FluentAssertions;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Xunit;

/// <summary>
/// Tests for clock synchronization architecture.
/// </summary>
public class ClockTests {
    /// <summary>
    /// Tests that CyclesClock correctly calculates CurrentDateTime from StartTime and cycles.
    /// </summary>
    [Fact]
    public void CyclesClock_CurrentDateTime_ShouldReflectStartTimePlusElapsed() {
        // Arrange
        State state = new State(CpuModel.INTEL_80286);
        ICyclesLimiter cyclesLimiter = new NullCyclesLimiter();
        CyclesClock clock = new CyclesClock(state, cyclesLimiter, 1000); // 1000 cycles per second
        DateTime startTime = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        clock.StartTime = startTime;

        // Act - simulate 2000 cycles (2 seconds)
        for (int i = 0; i < 2000; i++) {
            state.IncCycles();
        }
        DateTime currentDateTime = clock.CurrentDateTime;

        // Assert
        DateTime expectedDateTime = startTime.AddSeconds(2);
        currentDateTime.Should().BeCloseTo(expectedDateTime, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Tests that EmulatedClock StartTime can be set and CurrentDateTime is calculated correctly.
    /// </summary>
    [Fact]
    public void EmulatedClock_StartTime_CanBeSetAndCurrentDateTimeCalculated() {
        // Arrange
        DateTime startTime = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        ICyclesLimiter cyclesLimiter = new NullCyclesLimiter();
        EmulatedClock clock = new EmulatedClock(cyclesLimiter);
        
        // Act
        clock.StartTime = startTime;
        DateTime currentDateTime = clock.CurrentDateTime;

        // Assert - CurrentDateTime should be StartTime plus elapsed time
        // Since the stopwatch has been running, it should be after StartTime
        currentDateTime.Should().BeOnOrAfter(startTime);
    }

    /// <summary>
    /// Tests that StartTime can be set and retrieved correctly.
    /// </summary>
    [Fact]
    public void Clock_StartTime_CanBeSetAndRetrieved() {
        // Arrange
        State state = new State(CpuModel.INTEL_80286);
        ICyclesLimiter cyclesLimiter = new NullCyclesLimiter();
        CyclesClock clock = new CyclesClock(state, cyclesLimiter, 1000);
        DateTime expectedStartTime = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Act
        clock.StartTime = expectedStartTime;
        DateTime actualStartTime = clock.StartTime;

        // Assert
        actualStartTime.Should().Be(expectedStartTime);
    }

    /// <summary>
    /// Tests that pause/resume methods can be called without throwing exceptions.
    /// </summary>
    [Fact]
    public void Clock_OnPauseAndOnResume_ShouldNotThrow() {
        // Arrange
        State state = new State(CpuModel.INTEL_80286);
        ICyclesLimiter cyclesLimiter = new NullCyclesLimiter();
        CyclesClock clock = new CyclesClock(state, cyclesLimiter, 1000);

        // Act
        Action act = () => {
            clock.OnPause();
            clock.OnResume();
        };

        // Assert
        act.Should().NotThrow();
    }
}
