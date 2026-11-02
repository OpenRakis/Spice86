namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Xunit;

public sealed class CpuCycleLimiterTests {
    [Theory]
    [InlineData(0, 3000)]
    [InlineData(-1, 3000)]
    [InlineData(1, 100)]
    [InlineData(3000, 3000)]
    [InlineData(70000, 60000)]
    public void ConstructorPreservesDefaultRateAndSupportedBounds(int requestedRate, int expectedRate) {
        // Arrange
        State state = new(CpuModel.INTEL_80286);

        // Act
        CpuCycleLimiter limiter = new(state, requestedRate);

        // Assert
        limiter.TargetCpuCyclesPerMs.Should().Be(expectedRate);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(1, 100)]
    [InlineData(3000, 3000)]
    [InlineData(70000, 60000)]
    public void RateChangesRemainWithinSupportedBounds(int requestedRate, int expectedRate) {
        // Arrange
        State state = new(CpuModel.INTEL_80286);
        CpuCycleLimiter limiter = new(state, 3000);

        // Act
        limiter.TargetCpuCyclesPerMs = requestedRate;

        // Assert
        limiter.TargetCpuCyclesPerMs.Should().Be(expectedRate);
        Action regulate = limiter.RegulateCycles;
        regulate.Should().NotThrow();
    }
}
