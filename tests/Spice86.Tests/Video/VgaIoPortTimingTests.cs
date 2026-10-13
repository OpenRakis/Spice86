namespace Spice86.Tests.Video;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers.General;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;

using Xunit;

public sealed class VgaIoPortTimingTests {
    [Theory]
    [InlineData(0x3DA, 399.0, 0)]
    [InlineData(0x3DA, 400.25, 1)]
    [InlineData(0x3DA, 411.75, 1)]
    [InlineData(0x3DA, 412.25, 9)]
    [InlineData(0x3BA, 413.0, 9)]
    [InlineData(0x3DA, 414.25, 1)]
    [InlineData(0x3DA, 449.0, 0)]
    [InlineData(0x3DA, 0.81, 1)]
    [InlineData(0x3DA, 0.79, 0)]
    [InlineData(0x3DA, 0.99, 0)]
    public void InputStatusTracksBeamWithoutPumpingRenderer(int port, double scanline, byte expectedStatus) {
        // Arrange
        State cpuState = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), cpuState, NullLogger.Instance, true);
        VideoState state = CreateVideoState();
        state.GeneralRegisters.InputStatusRegister1.Value = 0xA9;
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        clock.ElapsedTimeMs.Returns(scanline * 100 * 1000.0 / (25175000 / 8));
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);

        // Act
        byte status = ports.ReadByte((ushort)port);

        // Assert
        (status & 9).Should().Be(expectedStatus);
        (status & ~9).Should().Be(0xA0);
        state.GeneralRegisters.InputStatusRegister1.Value.Should().Be(0xA9);
    }

    [Theory]
    [InlineData(448.25, 9)]
    [InlineData(449.25, 8)]
    [InlineData(450.25, 0)]
    public void VerticalRetraceCanWrapAcrossFrameBoundary(double scanline, byte expectedStatus) {
        // Arrange
        State cpuState = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), cpuState, NullLogger.Instance, true);
        VideoState state = CreateVideoState();
        state.CrtControllerRegisters.VerticalSyncStart = 192;
        state.CrtControllerRegisters.VerticalSyncEndRegister.Value = 0x82;
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        clock.ElapsedTimeMs.Returns(scanline * 100 * 1000.0 / (25175000 / 8));
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);

        // Act
        byte status = ports.ReadByte(0x3DA);

        // Assert
        (status & 9).Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData(799.5, 0)]
    [InlineData(800.5, 1)]
    [InlineData(824.5, 9)]
    [InlineData(828.5, 1)]
    public void HalvedVerticalTimingDoublesPhysicalScanlineIntervals(double scanline, byte expectedStatus) {
        // Arrange
        State cpuState = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), cpuState, NullLogger.Instance, true);
        VideoState state = CreateVideoState();
        state.CrtControllerRegisters.CrtModeControlRegister.Value = 0xE7;
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        clock.ElapsedTimeMs.Returns(scanline * 100 * 1000.0 / (25175000 / 8));
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);

        // Act
        byte status = ports.ReadByte(0x3DA);

        // Assert
        (status & 9).Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData(MiscellaneousOutput.ClockSelectValue.Use25175Khz, 8, false, 95, 0.031777557100297914)]
    [InlineData(MiscellaneousOutput.ClockSelectValue.Use28322Khz, 8, false, 95, 0.02824659328295928)]
    [InlineData(MiscellaneousOutput.ClockSelectValue.Use28322Khz, 9, false, 95, 0.0317774258251326)]
    [InlineData(MiscellaneousOutput.ClockSelectValue.Use25175Khz, 8, true, 95, 0.06355511420059583)]
    [InlineData(MiscellaneousOutput.ClockSelectValue.Use25175Khz, 8, false, 115, 0.03813306852035749)]
    [InlineData(MiscellaneousOutput.ClockSelectValue.External, 8, false, 95, 0.02824659328295928)]
    [InlineData(MiscellaneousOutput.ClockSelectValue.Reserved, 8, false, 95, 0.02824659328295928)]
    public void ProgrammedClocksAndHorizontalTotalDriveRetrace(
        MiscellaneousOutput.ClockSelectValue clockSelect, int dotsPerClock, bool halfDotClock,
        byte horizontalTotal, double lineDurationMs) {
        // Arrange
        State cpuState = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), cpuState, NullLogger.Instance, true);
        VideoState state = CreateVideoState();
        state.GeneralRegisters.MiscellaneousOutput.ClockSelect = clockSelect;
        state.SequencerRegisters.ClockingModeRegister.DotsPerClock = dotsPerClock;
        state.SequencerRegisters.ClockingModeRegister.HalfDotClock = halfDotClock;
        state.CrtControllerRegisters.HorizontalTotal = horizontalTotal;
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);
        clock.ElapsedTimeMs.Returns(412.5 * lineDurationMs);

        // Act
        byte status = ports.ReadByte(0x3DA);

        // Assert
        (status & 8).Should().Be(8);
    }

    [Fact]
    public void ClockWriteStartsNewBeamEpochWithoutPumpingRenderer() {
        // Arrange
        State cpuState = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), cpuState, NullLogger.Instance, true);
        VideoState state = CreateVideoState();
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);
        clock.ElapsedTimeMs.Returns(100.0);

        // Act
        ports.WriteByte(0x3C2, 4);
        clock.ElapsedTimeMs.Returns(100 + 412.5 * 800000.0 / 28322000);
        byte status = ports.ReadByte(0x3DA);

        // Assert
        (status & 8).Should().Be(8);
        VgaTimingParameters.Calculate(state).PixelClockHz.Should().Be(28322000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScreenOffAndRepeatedTimingWritesPreserveSyncPhase(bool screenOff) {
        // Arrange
        State cpuState = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), cpuState, NullLogger.Instance, true);
        VideoState state = CreateVideoState();
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);
        clock.ElapsedTimeMs.Returns(412.5 * 800000.0 / 25175000);

        // Act
        ports.WriteByte(0x3C4, 1);
        ports.WriteByte(0x3C5, screenOff ? (byte)0x21 : (byte)1);
        byte status = ports.ReadByte(0x3DA);

        // Assert
        (status & 8).Should().Be(8);
    }

    [Theory]
    [InlineData(0.75, 0)]
    [InlineData(0.85, 1)]
    [InlineData(1.01, 1)]
    [InlineData(1.05, 0)]
    public void HorizontalBlankingCanWrapAcrossLineBoundary(double scanline, byte expectedStatus) {
        // Arrange
        State cpuState = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), cpuState, NullLogger.Instance, true);
        VideoState state = CreateVideoState();
        state.CrtControllerRegisters.HorizontalBlankingEndRegister.HorizontalBlankingEnd = 8;
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);
        clock.ElapsedTimeMs.Returns(scanline * 800000.0 / 25175000);

        // Act
        byte status = ports.ReadByte(0x3DA);

        // Assert
        (status & 1).Should().Be(expectedStatus);
    }

    [Fact]
    public void EqualHorizontalBlankingCountersDoNotInventSixtyFourClocksOfBlanking() {
        // Arrange
        State cpuState = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), cpuState, NullLogger.Instance, true);
        VideoState state = CreateVideoState();
        state.CrtControllerRegisters.HorizontalBlankingEndRegister.HorizontalBlankingEnd = 16;
        state.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalBlankingEnd5 = 0;
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);
        clock.ElapsedTimeMs.Returns(0.85 * 800000.0 / 25175000);

        // Act
        byte status = ports.ReadByte(0x3DA);

        // Assert
        (status & 1).Should().Be(0);
    }

    [Theory]
    [InlineData(199.5, 0)]
    [InlineData(200.5, 1)]
    [InlineData(210.5, 1)]
    [InlineData(211.5, 0)]
    public void VerticalBlankingHasItsOwnIntervalWithinTheDisplayCounters(double scanline, byte expectedStatus) {
        // Arrange
        State cpuState = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), cpuState, NullLogger.Instance, true);
        VideoState state = CreateVideoState();
        state.CrtControllerRegisters.OverflowRegister.Value &= 0xF7;
        state.CrtControllerRegisters.VerticalBlankingStart = 199;
        state.CrtControllerRegisters.VerticalBlankingEnd = 210;
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);
        clock.ElapsedTimeMs.Returns(scanline * 800000.0 / 25175000);

        // Act
        byte status = ports.ReadByte(0x3DA);

        // Assert
        (status & 1).Should().Be(expectedStatus);
    }

    private static VideoState CreateVideoState() {
        VideoState state = new();
        state.SequencerRegisters.ClockingModeRegister.DotsPerClock = 8;
        state.CrtControllerRegisters.HorizontalTotal = 95;
        state.CrtControllerRegisters.HorizontalDisplayEnd = 79;
        state.CrtControllerRegisters.HorizontalBlankingStart = 80;
        state.CrtControllerRegisters.HorizontalBlankingEndRegister.Value = 0x82;
        state.CrtControllerRegisters.HorizontalSyncEndRegister.Value = 0x80;
        state.CrtControllerRegisters.VerticalTotal = 191;
        state.CrtControllerRegisters.OverflowRegister.Value = 0x1F;
        state.CrtControllerRegisters.VerticalDisplayEnd = 143;
        state.CrtControllerRegisters.VerticalSyncStart = 156;
        state.CrtControllerRegisters.VerticalSyncEndRegister.Value = 0x8E;
        state.CrtControllerRegisters.VerticalBlankingStart = 143;
        state.CrtControllerRegisters.VerticalBlankingEnd = 192;
        return state;
    }
}
