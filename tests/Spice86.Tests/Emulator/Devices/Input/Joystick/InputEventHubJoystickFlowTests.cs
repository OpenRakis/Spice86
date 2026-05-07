namespace Spice86.Tests.Emulator.Devices.Input.Joystick;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Interfaces;

using System;

using Xunit;

/// <summary>
/// End-to-end test: UI-side fake -> InputEventHub queue -> Gameport.
/// Mirrors how the production app flows joystick input from
/// MainWindowViewModel/HeadlessGui through InputEventHub onto the
/// emulator thread.
/// </summary>
public sealed class InputEventHubJoystickFlowTests {

    [Fact]
    public void EventsQueueOnUiThreadAndFlushOnEmulatorPump() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(new AddressReadWriteBreakpoints(),
            state, logger, false);
        FakeJoystickEventSource ui = new();
        InputEventHub hub = new(keyboardEvents: null, mouseEvents: null,
            joystickEvents: ui);
        FakeTimeProvider time = new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Gameport gameport = new(state, dispatcher, hub, time,
            rumbleRouter: null, midiRouter: null,
            failOnUnhandledPort: false, loggerService: logger);

        // UI raises events. They must be queued, NOT applied immediately.
        ui.RaiseConnect(0);
        ui.RaiseAxis(0, JoystickAxis.X, 0.75f);
        ui.RaiseButton(0, 0, true);

        gameport.GetCurrentState().StickA.IsConnected.Should().BeFalse(
            because: "events are still in the queue and have not been pumped");

        // Pump the queue (this is what EmulationLoop does on every iteration).
        hub.ProcessAllPendingInputEvents();

        VirtualJoystickState afterPump = gameport.GetCurrentState();
        afterPump.StickA.IsConnected.Should().BeTrue();
        afterPump.StickA.X.Should().BeApproximately(0.75f, 1e-6f);
        afterPump.StickA.IsButtonPressed(0).Should().BeTrue();
    }

    [Fact]
    public void PostJoystickAxisEvent_FlowsToGameport() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(new AddressReadWriteBreakpoints(),
            state, logger, false);
        InputEventHub hub = new(keyboardEvents: null, mouseEvents: null,
            joystickEvents: null); // no UI source -- only PostJoystickAxisEvent
        FakeTimeProvider time = new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Gameport gameport = new(state, dispatcher, hub, time,
            rumbleRouter: null, midiRouter: null,
            failOnUnhandledPort: false, loggerService: logger);

        hub.PostJoystickConnectionEvent(new JoystickConnectionEventArgs(0, true, "MCP"));
        hub.PostJoystickAxisEvent(new JoystickAxisEventArgs(0, JoystickAxis.Y, -0.5f));
        hub.ProcessAllPendingInputEvents();

        VirtualJoystickState snapshot = gameport.GetCurrentState();
        snapshot.StickA.IsConnected.Should().BeTrue();
        snapshot.StickA.Y.Should().BeApproximately(-0.5f, 1e-6f);
    }
}

