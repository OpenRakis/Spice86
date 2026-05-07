namespace Spice86.Tests.Emulator.Devices.Input.Joystick;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

using Xunit;

public sealed class GameportRouterWiringTests {
    private sealed class CapturingMpu401Sink : IMpu401DataSink {
        public List<(int basePort, byte value)> Writes { get; } = new();

        public void WriteData(int basePort, byte value) {
            Writes.Add((basePort, value));
        }
    }

    [Fact]
    public void WriteByteToPort201_ForwardsToMidiRouter_WhenEnabled() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(new AddressReadWriteBreakpoints(),
            state, logger, false);
        FakeJoystickEventSource events = new();
        FakeTimeProvider time = new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        CapturingMpu401Sink sink = new();
        MidiOnGameportRouter midi = new(sink, logger);
        midi.Configure(new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x330 });

        Gameport gameport = new(state, dispatcher, events, time,
            rumbleRouter: null, midiRouter: midi,
            failOnUnhandledPort: false, loggerService: logger);

        dispatcher.WriteByte(GameportConstants.Port201, 0xC9);
        dispatcher.WriteByte(GameportConstants.Port201, 0x40);

        sink.Writes.Should().Equal(
            new[] { (0x330, (byte)0xC9), (0x330, (byte)0x40) });
        gameport.MidiRouter.Should().BeSameAs(midi);
    }

    [Fact]
    public void WriteByteToPort201_DoesNotForward_WhenMidiRouterDisabled() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(new AddressReadWriteBreakpoints(),
            state, logger, false);
        FakeJoystickEventSource events = new();
        FakeTimeProvider time = new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        CapturingMpu401Sink sink = new();
        MidiOnGameportRouter midi = new(sink, logger);
        // Router left at default (disabled).

        Gameport gameport = new(state, dispatcher, events, time,
            rumbleRouter: null, midiRouter: midi,
            failOnUnhandledPort: false, loggerService: logger);

        dispatcher.WriteByte(GameportConstants.Port201, 0xC9);

        sink.Writes.Should().BeEmpty();
        gameport.Should().NotBeNull();
    }

    [Fact]
    public void Gameport_ExposesRumbleRouter_WhenWired() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(new AddressReadWriteBreakpoints(),
            state, logger, false);
        FakeJoystickEventSource events = new();
        FakeTimeProvider time = new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        RumbleRouter rumble = new(sink: null, logger);

        Gameport gameport = new(state, dispatcher, events, time,
            rumbleRouter: rumble, midiRouter: null,
            failOnUnhandledPort: false, loggerService: logger);

        gameport.RumbleRouter.Should().BeSameAs(rumble);
        gameport.MidiRouter.Should().BeNull();
    }
}
