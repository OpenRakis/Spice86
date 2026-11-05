namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.Mouse;

using Xunit;

public sealed class MouseDriverTests {
    private const ushort DriverStubSegment = 0xF000;
    private const ushort UserRoutineSegment = 0x2000;
    private const ushort UserRoutineOffset = 0x0010;
    private const ushort InterruptedSp = 0xFFF0;
    private const ushort InterruptedAx = 0x1234;
    private const ushort UserRoutineSp = 0xFFD0;
    private const ushort UserRoutineAx = 0x5678;

    private readonly State _state = new(CpuModel.INTEL_80286);
    private readonly SharedMouseData _sharedMouseData = new();
    private readonly InputEventHub _inputEventHub = new();
    private readonly MouseDriver _driver;
    private readonly MouseInt33Handler _handler;

    public MouseDriverTests() {
        Memory memory = new(new(), new Ram(A20Gate.EndOfHighMemoryArea), new A20Gate(false), new RealModeMmu386(), false);
        IOPortDispatcher ports = new(new(), _state, NullLogger.Instance, false);
        DualPic pic = new(ports, _state, NullLogger.Instance, false);
        CyclesClock clock = new(_state, 1000, null, DateTimeOffset.UnixEpoch);
        DeviceScheduler scheduler = new(clock, NullLogger.Instance, "Mouse driver test");
        Mouse mouse = new(_state, _sharedMouseData, pic, MouseType.Ps2, NullLogger.Instance, false, clock, scheduler, _inputEventHub);
        IVgaFunctionality video = Substitute.For<IVgaFunctionality>();
        video.GetCurrentMode().Returns(new VgaMode(MemoryModel.Packed, 320, 200, 8, 8, 8, 0xA000, 1.0));
        _driver = new MouseDriver(_state, _sharedMouseData, memory, mouse, video, NullLogger.Instance);
        _driver.WriteAssemblyInRam(new MemoryAsmWriter(memory, new(DriverStubSegment, 0),
            new CallbackHandler(_state, NullLogger.Instance)));
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

    [Fact]
    public void UserSubroutineEverRegistered_StaysTrueOnceANonZeroSubroutineIsRegistered() {
        // Arrange / Assert: a fresh driver reports false.
        _sharedMouseData.UserSubroutineEverRegistered.Should().BeFalse();

        // Act / Assert: registering a non-zero subroutine sets it.
        _driver.RegisterCallback(new MouseUserCallback(MouseEventMask.Movement, 0x1234, 0x5678));
        _sharedMouseData.UserSubroutineEverRegistered.Should().BeTrue();

        // Act / Assert: registering a zero subroutine afterwards keeps it true.
        _driver.RegisterCallback(new MouseUserCallback(MouseEventMask.Movement, 0, 0));
        _sharedMouseData.UserSubroutineEverRegistered.Should().BeTrue();
    }

    [Fact]
    public void UserRoutineRegisteredAtSegmentZeroOffsetIsCalledOnMatchingEvent() {
        // Arrange: a mouse reporting a movement event, a driver bound to it with its stub
        // written in RAM, and the int 33h handler registering a routine at 0x1234:0x0000.
        IMouseDevice mouse = Substitute.For<IMouseDevice>();
        mouse.LastTrigger.Returns(MouseEventMask.Movement);
        IVgaFunctionality video = Substitute.For<IVgaFunctionality>();
        video.GetCurrentMode().Returns(new VgaMode(MemoryModel.Packed, 320, 200, 8, 8, 8, 0xA000, 1.0));
        Memory memory = new(new(), new Ram(A20Gate.EndOfHighMemoryArea), new A20Gate(false),
            new RealModeMmu386(), false);
        MouseDriver driver = new(_state, _sharedMouseData, memory, mouse, video, NullLogger.Instance);
        MouseInt33Handler handler = new(memory, Substitute.For<IFunctionHandlerProvider>(),
            new Stack(memory, _state), _state, NullLogger.Instance, driver);
        MemoryAsmWriter asmWriter = new(memory, new SegmentedAddress(0xC000, 0),
            new CallbackHandler(_state, NullLogger.Instance));
        SegmentedAddress driverAddress = driver.WriteAssemblyInRam(asmWriter);
        _state.CX = (ushort)MouseEventMask.Movement;
        _state.ES = 0x1234;
        _state.DX = 0;
        handler.SetMouseUserDefinedSubroutine();

        // Act: a movement event fires, so the driver points the far call of its stub at the
        // registered routine.
        driver.BeforeUserHandlerExecution();

        // Assert: the stub is "far ret (default); callback; far call <target>; callback; far ret"
        // starting at driverAddress-1, so the far call target sits at driverAddress+5. A routine
        // registered at 0x1234:0x0000 is enabled (only 0:0 disables), so the call must reach it.
        memory.SegmentedAddress16[driverAddress.Linear + 5].Should()
            .Be(new SegmentedAddress(0x1234, 0), "the routine at 0x1234:0x0000 is called");
        _sharedMouseData.UserSubroutineEverRegistered.Should().BeTrue();
    }

    [Fact]
    public void NestedDriverPassLeavesTheRunningUserRoutineRegistersAlone() {
        // Arrange
        EnterUserRoutine();

        // Act: IRQ 12 re-enters the driver stub while the user routine runs. That pass must not call
        // the routine again, so it has nothing to restore.
        _driver.BeforeUserHandlerExecution();
        _driver.AfterUserHandlerExecution();

        // Assert
        _state.SP.Should().Be(UserRoutineSp, "the nested pass must not move the user routine's stack");
        _state.AX.Should().Be(UserRoutineAx, "the nested pass must not overwrite the user routine's registers");
    }

    [Fact]
    public void OuterDriverPassRestoresInterruptedRegistersAfterANestedPass() {
        // Arrange
        EnterUserRoutine();
        _driver.BeforeUserHandlerExecution();
        _driver.AfterUserHandlerExecution();
        _state.SP = UserRoutineSp;
        _state.AX = UserRoutineAx;

        // Act: the user routine returns to the outer pass.
        _driver.AfterUserHandlerExecution();

        // Assert
        _state.SP.Should().Be(InterruptedSp, "the outer pass restores the stack of the interrupted program");
        _state.AX.Should().Be(InterruptedAx, "the outer pass restores the registers of the interrupted program");
    }

    private void EnterUserRoutine() {
        _driver.RegisterCallback(new MouseUserCallback(MouseEventMask.Movement, UserRoutineSegment, UserRoutineOffset));
        _inputEventHub.PostMouseMoveEvent(new MouseMoveEventArgs(0.5, 0.5));
        _inputEventHub.ProcessAllPendingInputEvents();
        _state.SP = InterruptedSp;
        _state.AX = InterruptedAx;
        _driver.BeforeUserHandlerExecution();
        _state.SP = UserRoutineSp;
        _state.AX = UserRoutineAx;
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
