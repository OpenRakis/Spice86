namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using Xunit;

public sealed class MouseDriverTests {
    private readonly State _state = new(CpuModel.INTEL_80286);
    private readonly SharedMouseData _sharedMouseData = new();
    private readonly MouseDriver _driver;
    private readonly MouseInt33Handler _handler;

    public MouseDriverTests() {
        Memory memory = new(new(), new Ram(A20Gate.EndOfHighMemoryArea), new A20Gate(false), new RealModeMmu386(), false);
        IOPortDispatcher ports = new(new(), _state, NullLogger.Instance, false);
        DualPic pic = new(ports, _state, NullLogger.Instance, false);
        CyclesClock clock = new(_state, 1000, null, DateTimeOffset.UnixEpoch);
        DeviceScheduler scheduler = new(clock, NullLogger.Instance, "Mouse driver test");
        Mouse mouse = new(_state, _sharedMouseData, pic, MouseType.Ps2, NullLogger.Instance, false, clock, scheduler);
        IVgaFunctionality video = Substitute.For<IVgaFunctionality>();
        video.GetCurrentMode().Returns(new VgaMode(MemoryModel.Packed, 320, 200, 8, 8, 8, 0xA000, 1.0));
        _driver = new MouseDriver(_state, _sharedMouseData, memory, mouse, video, NullLogger.Instance);
        _handler = new MouseInt33Handler(memory, Substitute.For<IFunctionHandlerProvider>(),
            new Stack(memory, _state), _state, NullLogger.Instance, _driver);
    }

    [Theory]
    [InlineData(10, 638, 5, 195, 0, 0, 10, 5)]
    [InlineData(10, 638, 5, 195, 333, 129, 333, 129)]
    [InlineData(10, 638, 5, 195, 639, 199, 638, 195)]
    [InlineData(638, 10, 195, 5, 0, 0, 10, 5)]
    [InlineData(638, 10, 195, 5, 333, 129, 333, 129)]
    [InlineData(638, 10, 195, 5, 639, 199, 638, 195)]
    [InlineData(638, 10, 5, 195, 10, 5, 10, 5)]
    [InlineData(10, 638, 195, 5, 638, 195, 638, 195)]
    [InlineData(42, 42, 17, 17, 0, 0, 42, 17)]
    [InlineData(42, 42, 17, 17, 639, 199, 42, 17)]
    public void SetPosition_ClampsToRangeRegardlessOfEndpointOrder(
        ushort firstX, ushort secondX, ushort firstY, ushort secondY,
        ushort x, ushort y, ushort expectedX, ushort expectedY) {
        // Arrange
        SetRanges(firstX, secondX, firstY, secondY);

        // Act
        _state.CX = x;
        _state.DX = y;
        _handler.SetMouseCursorPosition();
        _handler.GetMousePositionAndStatus();

        // Assert
        _state.CX.Should().Be(expectedX);
        _state.DX.Should().Be(expectedY);
        _driver.CurrentMinX.Should().Be(Math.Min(firstX, secondX));
        _driver.CurrentMaxX.Should().Be(Math.Max(firstX, secondX));
        _driver.CurrentMinY.Should().Be(Math.Min(firstY, secondY));
        _driver.CurrentMaxY.Should().Be(Math.Max(firstY, secondY));
        _sharedMouseData.MouseXRelative.Should().BeInRange(0.0, 1.0);
        _sharedMouseData.MouseYRelative.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public void Reset_RestoresFullScreenRangeBeforeCenteringCursor() {
        // Arrange
        SetRanges(10, 20, 5, 15);

        // Act
        _handler.MouseInstalledFlag();

        // Assert
        _driver.CurrentMinX.Should().Be(0);
        _driver.CurrentMaxX.Should().Be(639);
        _driver.CurrentMinY.Should().Be(0);
        _driver.CurrentMaxY.Should().Be(199);
        _sharedMouseData.MouseXRelative.Should().Be(320.0 / 639);
        _sharedMouseData.MouseYRelative.Should().Be(100.0 / 199);
    }

    private void SetRanges(ushort firstX, ushort secondX, ushort firstY, ushort secondY) {
        _state.CX = firstX;
        _state.DX = secondX;
        _handler.SetMouseHorizontalMinMaxPosition();
        _state.CX.Should().Be(firstX);
        _state.DX.Should().Be(secondX);

        _state.CX = firstY;
        _state.DX = secondY;
        _handler.SetMouseVerticalMinMaxPosition();
        _state.CX.Should().Be(firstY);
        _state.DX.Should().Be(secondY);
    }
}
