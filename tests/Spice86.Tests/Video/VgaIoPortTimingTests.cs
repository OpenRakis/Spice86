namespace Spice86.Tests.Video;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
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
        clock.ElapsedTimeMs.Returns(scanline * 1000.0 / 31469.0);
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
        clock.ElapsedTimeMs.Returns(scanline * 1000.0 / 31469.0);
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
        clock.ElapsedTimeMs.Returns(scanline * 1000.0 / 31469.0);
        _ = new VgaIoPortHandler(cpuState, ports, NullLogger.Instance, state, clock, true);

        // Act
        byte status = ports.ReadByte(0x3DA);

        // Assert
        (status & 9).Should().Be(expectedStatus);
    }

    private static VideoState CreateVideoState() {
        VideoState state = new();
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
        return state;
    }
}
