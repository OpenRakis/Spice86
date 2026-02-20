namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Clock;

using Xunit;

/// <summary>
/// Tests for clock synchronization architecture.
/// </summary>
public class ClockTests {
    /// <summary>
    /// Tests that EmulatedClock with cycle-based timing correctly calculates CurrentDateTime.
    /// </summary>
    [Fact]
    public void EmulatedClock_CycleBased_CurrentDateTime_ShouldReflectStartTimePlusElapsed() {
        // Arrange
        State state = new State(CpuModel.INTEL_80286);
        EmulatedClock clock = new EmulatedClock(state, 1000); // 1000 cycles per second
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
    /// Tests that StartTime can be set and retrieved correctly.
    /// </summary>
    [Fact]
    public void EmulatedClock_StartTime_CanBeSetAndRetrieved() {
        // Arrange
        State state = new State(CpuModel.INTEL_80286);
        EmulatedClock clock = new EmulatedClock(state, 1000);
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
    public void EmulatedClock_OnPauseAndOnResume_ShouldNotThrow() {
        // Arrange
        State state = new State(CpuModel.INTEL_80286);
        EmulatedClock clock = new EmulatedClock(state, 1000);

        // Act
        Action act = () => {
            clock.OnPause();
            clock.OnResume();
        };

        // Assert
        act.Should().NotThrow();
    }
}
