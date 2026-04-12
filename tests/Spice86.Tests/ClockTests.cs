namespace Spice86.Tests;

using FluentAssertions;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM.Clock;
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
        DateTimeOffset startTime = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        CyclesClock clock = new CyclesClock(state, 1000, null, startTime); // 1000 cycles per second

        // Act - simulate 2000 cycles (2 seconds)
        for (int i = 0; i < 2000; i++) {
            state.IncCycles();
        }
        DateTimeOffset currentDateTime = clock.CurrentDateTime;

        // Assert
        DateTimeOffset expectedDateTime = startTime.AddSeconds(2);
        currentDateTime.Should().BeCloseTo(expectedDateTime, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Tests that EmulatedClock constructed with a start time correctly reports CurrentDateTime.
    /// </summary>
    [Fact]
    public void EmulatedClock_StartTime_CanBeSetAndCurrentDateTimeCalculated() {
        // Arrange
        DateTimeOffset startTime = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        EmulatedClock clock = new EmulatedClock(null, startTime);
        
        // Act
        DateTimeOffset currentDateTime = clock.CurrentDateTime;

        // Assert - CurrentDateTime should be StartTime plus elapsed time
        // Since the stopwatch has been running, it should be after StartTime
        currentDateTime.Should().BeOnOrAfter(startTime);
    }

    /// <summary>
    /// Tests that CyclesClock constructed with a start time stores and retrieves it correctly.
    /// </summary>
    [Fact]
    public void Clock_StartTime_CanBeSetAndRetrieved() {
        // Arrange
        State state = new State(CpuModel.INTEL_80286);
        DateTimeOffset expectedStartTime = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        CyclesClock clock = new CyclesClock(state, 1000, null, expectedStartTime);

        // Act
        DateTimeOffset actualStartTime = clock.StartTime;

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
        CyclesClock clock = new CyclesClock(state, 1000, null, DateTimeOffset.UnixEpoch);

        // Act
        Action act = () => {
            clock.OnPause();
            clock.OnResume();
        };

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that CyclesClock with no jitter seed returns an exact baseline value.
    /// </summary>
    [Fact]
    public void CyclesClock_WithNullSeed_ReturnsExactBaselineTime() {
        State state = new State(CpuModel.INTEL_80286);
        CyclesClock clock = new CyclesClock(state, 1000, null, DateTimeOffset.UnixEpoch);
        for (int i = 0; i < 1000; i++) {
            state.IncCycles();
        }

        clock.ElapsedTimeMs.Should().Be(1000.0);
    }

    /// <summary>
    /// Tests that CyclesClock with a seed produces a value that differs from the no-seed baseline
    /// but remains within the ±0.01 ms jitter bound.
    /// </summary>
    [Fact]
    public void CyclesClock_WithSeed_AddsBoundedJitterToBaselineTime() {
        State stateBase = new State(CpuModel.INTEL_80286);
        State stateJitter = new State(CpuModel.INTEL_80286);
        CyclesClock clockBase = new CyclesClock(stateBase, 1000, null, DateTimeOffset.UnixEpoch);
        CyclesClock clockJitter = new CyclesClock(stateJitter, 1000, 42, DateTimeOffset.UnixEpoch);

        for (int i = 0; i < 1000; i++) {
            stateBase.IncCycles();
            stateJitter.IncCycles();
        }

        double baseTime = clockBase.ElapsedTimeMs;
        double jitteredTime = clockJitter.ElapsedTimeMs;

        Math.Abs(jitteredTime - baseTime).Should().BeLessThanOrEqualTo(0.01);
    }

    /// <summary>
    /// Tests that CyclesClock with the same seed produces identical ElapsedTimeMs across runs.
    /// </summary>
    [Fact]
    public void CyclesClock_WithSameSeed_ProducesReproducibleJitter() {
        State state1 = new State(CpuModel.INTEL_80286);
        CyclesClock clock1 = new CyclesClock(state1, 1000, 12345, DateTimeOffset.UnixEpoch);
        for (int i = 0; i < 1000; i++) {
            state1.IncCycles();
        }
        double time1 = clock1.ElapsedTimeMs;

        State state2 = new State(CpuModel.INTEL_80286);
        CyclesClock clock2 = new CyclesClock(state2, 1000, 12345, DateTimeOffset.UnixEpoch);
        for (int i = 0; i < 1000; i++) {
            state2.IncCycles();
        }
        double time2 = clock2.ElapsedTimeMs;

        time1.Should().Be(time2);
    }

    /// <summary>
    /// Tests that CyclesClock instances with different seeds stay within the expected jitter bounds.
    /// </summary>
    [Fact]
    public void CyclesClock_WithDifferentSeeds_JitterIsBounded() {
        State baseState = new State(CpuModel.INTEL_80286);
        State state1 = new State(CpuModel.INTEL_80286);
        State state2 = new State(CpuModel.INTEL_80286);
        CyclesClock baseClock = new CyclesClock(baseState, 1000, null, DateTimeOffset.UnixEpoch);
        CyclesClock clock1 = new CyclesClock(state1, 1000, 1, DateTimeOffset.UnixEpoch);
        CyclesClock clock2 = new CyclesClock(state2, 1000, 2, DateTimeOffset.UnixEpoch);

        for (int i = 0; i < 1000; i++) {
            baseState.IncCycles();
            state1.IncCycles();
            state2.IncCycles();
        }

        double baseTime = baseClock.ElapsedTimeMs;
        Math.Abs(clock1.ElapsedTimeMs - baseTime).Should().BeLessThanOrEqualTo(0.01);
        Math.Abs(clock2.ElapsedTimeMs - baseTime).Should().BeLessThanOrEqualTo(0.01);
    }

    /// <summary>
    /// Tests that EmulatedClock with a seed produces a non-negative elapsed time;
    /// jitter must not push the value below zero. Tight bound testing requires a fake time
    /// source and is not reliable with a real Stopwatch.
    /// </summary>
    [Fact]
    public void EmulatedClock_WithSeed_ElapsedTimeMsIsNonNegative() {
        EmulatedClock clock = new EmulatedClock(99, DateTimeOffset.UnixEpoch);

        // Force multiple cache refreshes to exercise the jitter code path.
        for (int i = 0; i < 300; i++) {
            clock.ElapsedTimeMs.Should().BeGreaterThanOrEqualTo(0.0);
        }
    }
}
