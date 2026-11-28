namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
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
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

using System.Text.Json;

using Xunit;

/// <summary>
/// Checks state capture and restore of the INT 33h mouse driver and the mouse device it reads, on a whole
/// machine: capture, change the state through INT 33h and host input, restore, and compare what INT 33h reports
/// with what it reported at capture.
/// </summary>
public sealed class MouseDriverStateTests : IDisposable {
    private const byte MouseVector = 0x33;
    private const ushort LeftButtonIndex = 0;
    private const ushort DriverStubSegment = 0xF000;

    private readonly Spice86Creator _creator;
    private readonly Spice86DependencyInjection _dependencyInjection;
    private readonly Machine _machine;
    private readonly State _state;
    private readonly MouseInt33Handler _int33h;

    public MouseDriverStateTests() {
        _creator = new Spice86Creator("externalint", installInterruptVectors: true);
        _dependencyInjection = _creator.Create();
        _machine = _dependencyInjection.Machine;
        _state = _machine.CpuState;
        _int33h = (MouseInt33Handler)_machine.InstalledInterruptHandlers.Handlers[MouseVector];
    }

    public void Dispose() {
        _dependencyInjection.Dispose();
        _creator.Dispose();
    }

    [Fact]
    public void RestoreAfterChangesThroughInt33hGivesBackWhatInt33hReportedAtCapture() {
        // Arrange
        ConfigureThroughInt33h();
        MachineDeviceState captured = _machine.CaptureDeviceState(MachineDevices.Mouse);
        Int33hReport atCapture = ReadThroughInt33h();
        _machine.RestoreDeviceState(captured);

        // Act
        ChangeThroughInt33hAndInput();
        Int33hReport changed = ReadThroughInt33h();
        _machine.RestoreDeviceState(captured);
        Int33hReport restored = ReadThroughInt33h();

        // Assert
        changed.Should().NotBe(atCapture);
        restored.Should().Be(atCapture);
    }

    [Fact]
    public void OneStateRestoredTwiceGivesTheSameReportBothTimes() {
        // Arrange
        ConfigureThroughInt33h();
        MachineDeviceState captured = _machine.CaptureDeviceState(MachineDevices.Mouse);
        Int33hReport atCapture = ReadThroughInt33h();

        // Act
        _machine.RestoreDeviceState(captured);
        ChangeThroughInt33hAndInput();
        _machine.RestoreDeviceState(captured);
        Int33hReport first = ReadThroughInt33h();
        ChangeThroughInt33hAndInput();
        _machine.RestoreDeviceState(captured);
        Int33hReport second = ReadThroughInt33h();

        // Assert
        first.Should().Be(atCapture);
        second.Should().Be(atCapture);
    }

    [Fact]
    public void StateRestoredAfterAJsonRoundTripGivesBackWhatInt33hReportedAtCapture() {
        // Arrange
        ConfigureThroughInt33h();
        MachineDeviceState captured = _machine.CaptureDeviceState(MachineDevices.Mouse);
        string json = JsonSerializer.Serialize(captured);
        Int33hReport atCapture = ReadThroughInt33h();
        ChangeThroughInt33hAndInput();

        // Act
        MachineDeviceState? roundTripped = JsonSerializer.Deserialize<MachineDeviceState>(json);
        _machine.RestoreDeviceState(roundTripped ?? throw new InvalidOperationException("No state was read."));

        // Assert
        ReadThroughInt33h().Should().Be(atCapture);
        roundTripped.MouseDevice.Should().Be(captured.MouseDevice);
        roundTripped.MouseDriver.Should().BeEquivalentTo(captured.MouseDriver);
    }

    [Fact]
    public void RestoreGivesBackTheCursorVisibilityCounterAndTellsTheGui() {
        // Arrange: the driver starts hidden at -1; one show makes the cursor visible.
        IGuiMouseEvents gui = Substitute.For<IGuiMouseEvents>();
        MouseDriver driver = CreateStandaloneDriver(new State(CpuModel.INTEL_80286), gui);
        driver.ShowMouseCursor();
        MouseDriverState visible = driver.CaptureState();
        driver.HideMouseCursor();
        driver.HideMouseCursor();
        gui.ClearReceivedCalls();

        // Act
        driver.RestoreState(visible);

        // Assert: the GUI shows the cursor again, and one hide hides it as it did at capture.
        gui.Received(1).ShowMouseCursor();
        driver.CaptureState().CursorHiddenCounter.Should().Be(0);
        driver.HideMouseCursor();
        gui.Received(1).HideMouseCursor();
    }

    [Fact]
    public void RestoreGivesBackTheRegistersSavedForARunningUserRoutine() {
        // Arrange: a user routine runs with the registers of the interrupted program saved.
        State state = new(CpuModel.INTEL_80286);
        IGuiMouseEvents gui = Substitute.For<IGuiMouseEvents>();
        MouseDriver driver = CreateStandaloneDriver(state, gui);
        driver.RegisterCallback(new MouseUserCallback(MouseEventMask.Movement, 0x2000, 0x0010));
        gui.MouseMoved += Raise.Event<EventHandler<MouseMoveEventArgs>>(gui, new MouseMoveEventArgs(0.25, 0.25));
        state.AX = 0x1234;
        state.SP = 0xFFF0;
        driver.BeforeUserHandlerExecution();
        MouseDriverState inUserRoutine = driver.CaptureState();
        driver.AfterUserHandlerExecution();

        // Act
        driver.RestoreState(inUserRoutine);
        state.AX = 0x5678;
        state.SP = 0xFFD0;
        driver.AfterUserHandlerExecution();

        // Assert
        inUserRoutine.IsUserRoutineRunning.Should().BeTrue();
        state.AX.Should().Be(0x1234);
        state.SP.Should().Be(0xFFF0);
    }

    private void ConfigureThroughInt33h() {
        CallInt33h(0x0007, 0, 10, 600);
        CallInt33h(0x0008, 0, 20, 180);
        CallInt33h(0x0004, 0, 300, 90);
        CallInt33h(0x000F, 0, 24, 40);
        CallInt33h(0x0013, 0, 0, 32);
        _state.ES = 0x2000;
        CallInt33h(0x000C, 0, (ushort)MouseEventMask.LeftButtonDown, 0x0010);
        PressLeftButton(true);
        MoveMouse(0.75, 0.25);
    }

    private void ChangeThroughInt33hAndInput() {
        MoveMouse(0.1, 0.9);
        PressLeftButton(false);
        CallInt33h(0x0007, 0, 0, 100);
        CallInt33h(0x0008, 0, 0, 50);
        CallInt33h(0x000F, 0, 64, 64);
        CallInt33h(0x0013, 0, 0, 128);
        _state.ES = 0x3000;
        CallInt33h(0x000C, 0, (ushort)MouseEventMask.Movement, 0x0040);
    }

    /// <summary>
    /// Reads what INT 33h reports. Reading the counters and the motion resets them and reading the handler swaps
    /// it, so each read follows a capture or a restore.
    /// </summary>
    private Int33hReport ReadThroughInt33h() {
        CallInt33h(0x0003, 0, 0, 0);
        (ushort, ushort, ushort) status = (_state.BX, _state.CX, _state.DX);
        CallInt33h(0x0005, LeftButtonIndex, 0, 0);
        (ushort, ushort, ushort, ushort) presses = (_state.AX, _state.BX, _state.CX, _state.DX);
        CallInt33h(0x0006, LeftButtonIndex, 0, 0);
        (ushort, ushort, ushort, ushort) releases = (_state.AX, _state.BX, _state.CX, _state.DX);
        CallInt33h(0x000B, 0, 0, 0);
        (ushort, ushort) motion = (_state.CX, _state.DX);
        CallInt33h(0x001B, 0, 0, 0);
        (ushort, ushort, ushort) sensitivity = (_state.BX, _state.CX, _state.DX);
        _state.ES = 0;
        CallInt33h(0x0014, 0, 0, 0);
        (ushort, ushort, ushort) handler = (_state.CX, _state.ES, _state.DX);
        return new Int33hReport(status, presses, releases, motion, sensitivity, handler);
    }

    private void CallInt33h(ushort function, ushort bx, ushort cx, ushort dx) {
        _state.AX = function;
        _state.BX = bx;
        _state.CX = cx;
        _state.DX = dx;
        _int33h.Run();
    }

    private void MoveMouse(double x, double y) {
        _machine.InputEventHub.PostMouseMoveEvent(new MouseMoveEventArgs(x, y));
        _machine.InputEventHub.ProcessAllPendingInputEvents();
    }

    private void PressLeftButton(bool down) {
        _machine.InputEventHub.PostMouseButtonEvent(new MouseButtonEventArgs(MouseButton.Left, down));
        _machine.InputEventHub.ProcessAllPendingInputEvents();
    }

    private static MouseDriver CreateStandaloneDriver(State state, IGuiMouseEvents gui) {
        Memory memory = new(new(), new Ram(A20Gate.EndOfHighMemoryArea), new A20Gate(false), new RealModeMmu386(), false);
        IOPortDispatcher ports = new(new(), state, NullLogger.Instance, false);
        DualPic pic = new(ports, state, NullLogger.Instance, false);
        CyclesClock clock = new(state, 1000, null, DateTimeOffset.UnixEpoch);
        DeviceScheduler scheduler = new(clock, NullLogger.Instance, "Mouse driver state test");
        SharedMouseData sharedMouseData = new();
        Mouse mouse = new(state, sharedMouseData, pic, MouseType.Ps2, NullLogger.Instance, false, clock, scheduler, gui);
        IVgaFunctionality video = Substitute.For<IVgaFunctionality>();
        video.GetCurrentMode().Returns(new VgaMode(MemoryModel.Packed, 320, 200, 8, 8, 8, 0xA000, 1.0));
        MouseDriver driver = new(state, sharedMouseData, memory, mouse, video, NullLogger.Instance, ICallTargetRegistry.None, new Spice86.Core.Emulator.ReverseEngineer.RecordedRunFacts(), gui);
        driver.WriteAssemblyInRam(new MemoryAsmWriter(memory, new(DriverStubSegment, 0),
            new CallbackHandler(state, NullLogger.Instance)));
        return driver;
    }

    private sealed record Int33hReport(
        (ushort Buttons, ushort X, ushort Y) Status,
        (ushort Buttons, ushort Count, ushort X, ushort Y) LeftPresses,
        (ushort Buttons, ushort Count, ushort X, ushort Y) LeftReleases,
        (ushort X, ushort Y) Motion,
        (ushort Horizontal, ushort Vertical, ushort Threshold) Sensitivity,
        (ushort Mask, ushort Segment, ushort Offset) Handler);
}
