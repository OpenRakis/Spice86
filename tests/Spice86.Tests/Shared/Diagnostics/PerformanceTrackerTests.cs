namespace Spice86.Tests.Shared.Diagnostics;

using Xunit;

using FluentAssertions;

using Spice86.Shared.Diagnostics;
using Spice86.Shared.Interfaces;

using System;

public class PerformanceTrackerTests {
    private readonly TestTimeProvider _timeProvider;
    private readonly PerformanceTracker _performanceTracker;

    public PerformanceTrackerTests() {
        _timeProvider = new TestTimeProvider(new DateTime(2023, 1, 1, 12, 0, 0));
        _performanceTracker = new PerformanceTracker(_timeProvider);
    }

    [Fact]
    public void Update_ShouldCalculateIPS_WhenRunningNormally() {
        // Initial state
        _performanceTracker.InstructionsPerSecond.Should().Be(0);

        // Advance time by 1 second and cycles by 1000
        AdvanceTime(TimeSpan.FromSeconds(1));
        _performanceTracker.Update(1000);

        _performanceTracker.InstructionsPerSecond.Should().Be(1000);
    }

    [Fact]
    public void Update_ShouldIgnoreTimeSpentPaused() {
        // Run for 1 second, 1000 cycles
        AdvanceTime(TimeSpan.FromSeconds(1));
        _performanceTracker.Update(1000);

        // Pause for 5 seconds
        _performanceTracker.OnPause();
        AdvanceTime(TimeSpan.FromSeconds(5));

        // Resume
        _performanceTracker.OnResume();

        // Run for another 1 second, +1000 cycles (total 2000)
        AdvanceTime(TimeSpan.FromSeconds(1));
        _performanceTracker.Update(2000);

        // The delta is 1000 cycles over 1 second of ACTIVE time (5s pause ignored)
        _performanceTracker.InstructionsPerSecond.Should().Be(1000);
    }

    [Fact]
    public void OnPause_ShouldResetMetricsToZero() {
        _performanceTracker.OnPause();
        _performanceTracker.InstructionsPerSecond.Should().Be(0);
    }

    [Fact]
    public void Update_ShouldNotUpdate_WhenPaused() {
        _performanceTracker.OnPause();
        AdvanceTime(TimeSpan.FromSeconds(1));

        // This update should get ignored
        _performanceTracker.Update(1000);

        _performanceTracker.InstructionsPerSecond.Should().Be(0);
    }

    [Fact]
    public void OnResume_ShouldNotAdjustIfWasNotPaused() {
        // Should not throw or cause weird shifts
        _performanceTracker.OnResume();
    }

    private void AdvanceTime(TimeSpan span) {
        _timeProvider.CurrentTime += span;
    }

    private class TestTimeProvider : ITimeProvider {
        public DateTime CurrentTime { get; set; }

        public TestTimeProvider(DateTime startTime) {
            CurrentTime = startTime;
        }

        public DateTime Now => CurrentTime;
    }
}




