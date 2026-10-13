namespace Spice86.Tests.Video;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Video;

using Xunit;

public sealed class VgaTimingParametersTests {
    [Theory]
    [InlineData(false, 449, 400)]
    [InlineData(true, 898, 800)]
    public void VerticalClockDividerDoublesPhysicalFrameIntervals(
        bool verticalDivider, int totalLines, int displayLines) {
        // Arrange
        VideoState state = CreateState();
        state.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved = verticalDivider;

        // Act
        VgaTimingParameters timing = VgaTimingParameters.Calculate(state);

        // Assert
        timing.TotalScanlines.Should().Be(totalLines);
        timing.VisibleScanlines.Should().Be(displayLines);
        timing.LineDurationMs.Should().BeApproximately(800000.0 / 25175000, 1e-12);
        timing.FrameDurationMs.Should().BeApproximately(totalLines * 800000.0 / 25175000, 1e-12);
    }

    [Fact]
    public void ZeroRetraceDistancesMeanFullCounterPeriodsButZeroHorizontalBlankingDoesNot() {
        // Arrange
        VideoState state = CreateState();
        state.CrtControllerRegisters.HorizontalBlankingStart = 80;
        state.CrtControllerRegisters.HorizontalBlankingEndRegister.HorizontalBlankingEnd = 16;
        state.CrtControllerRegisters.HorizontalSyncStart = 84;
        state.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalSyncEnd = 20;
        state.CrtControllerRegisters.VerticalSyncStart = 156;
        state.CrtControllerRegisters.VerticalSyncEndRegister.VerticalSyncEnd = 12;
        state.CrtControllerRegisters.OverflowRegister.Value |= 4;

        // Act
        VgaTimingParameters timing = VgaTimingParameters.Calculate(state);

        // Assert
        timing.HorizontalBlankingEndMs.Should().Be(timing.HorizontalBlankingStartMs);
        (timing.HorizontalRetraceEndMs - timing.HorizontalRetraceStartMs)
            .Should().BeApproximately(32 * 8000.0 / 25175000, 1e-12);
        (timing.VerticalRetraceEndMs - timing.VerticalRetraceStartMs)
            .Should().BeApproximately(16 * 800000.0 / 25175000, 1e-12);
    }

    [Theory]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 10, 0, 11)]
    [InlineData(143, 64, 400, 449)]
    [InlineData(143, 16, 400, 529)]
    public void VerticalBlankingUsesItsOwnSevenBitEndCounter(
        byte start, byte end, int expectedStart, int expectedEnd) {
        // Arrange
        VideoState state = CreateState();
        if (start == 0) {
            state.CrtControllerRegisters.OverflowRegister.Value &= 0xF7;
        }
        state.CrtControllerRegisters.VerticalBlankingStart = start;
        state.CrtControllerRegisters.VerticalBlankingEnd = end;

        // Act
        VgaTimingParameters timing = VgaTimingParameters.Calculate(state);

        // Assert
        timing.VerticalBlankingStartMs.Should().BeApproximately(expectedStart * 800000.0 / 25175000, 1e-12);
        timing.VerticalBlankingEndMs.Should().BeApproximately(expectedEnd * 800000.0 / 25175000, 1e-12);
    }

    [Fact]
    public void NonTimingWritesKeepTheSameParametersAndFrameEpoch() {
        // Arrange
        VideoState state = CreateState();
        VgaTimingParameters before = state.BeamTiming.StartFrame(100);
        state.CrtControllerRegisters.ScreenStartAddress = 1234;
        state.CrtControllerRegisters.VerticalSyncEndRegister.Value |= 0x80;
        state.SequencerRegisters.ClockingModeRegister.ScreenOff = true;

        // Act
        VgaTimingParameters after = state.BeamTiming.Refresh(101);
        byte status = state.BeamTiming.ReadStatus(100 + 412.5 * 800000.0 / 25175000);

        // Assert
        after.Should().BeSameAs(before);
        (status & 8).Should().Be(8);
    }

    [Fact]
    public void TimingCalculationUsesOnlyTheCapturedRegisters() {
        // Arrange
        VideoState state = CreateState();
        VgaTimingRegisters registers = VgaTimingRegisters.Read(state);
        state.CrtControllerRegisters.HorizontalTotal = 115;
        state.SequencerRegisters.ClockingModeRegister.HalfDotClock = true;

        // Act
        VgaTimingParameters timing = VgaTimingParameters.Calculate(registers);

        // Assert
        timing.HorizontalTotal.Should().Be(100);
        timing.LineDurationMs.Should().BeApproximately(800000.0 / 25175000, 1e-12);
    }

    private static VideoState CreateState() {
        VideoState state = new();
        state.SequencerRegisters.ClockingModeRegister.DotsPerClock = 8;
        state.CrtControllerRegisters.HorizontalTotal = 95;
        state.CrtControllerRegisters.HorizontalDisplayEnd = 79;
        state.CrtControllerRegisters.VerticalTotal = 191;
        state.CrtControllerRegisters.OverflowRegister.Value = 0x1F;
        state.CrtControllerRegisters.VerticalDisplayEnd = 143;
        state.CrtControllerRegisters.VerticalSyncStart = 156;
        state.CrtControllerRegisters.VerticalSyncEndRegister.VerticalSyncEnd = 14;
        return state;
    }
}
