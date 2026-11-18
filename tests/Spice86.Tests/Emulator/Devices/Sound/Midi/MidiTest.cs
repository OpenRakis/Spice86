namespace Spice86.Tests.Emulator.Devices.Sound.Midi;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;

using Xunit;

/// <summary>
/// Tests the MPU-401 handshake of <see cref="Midi"/>. The command port is <c>0x331</c>
/// (<see cref="Midi.StatusPort"/>): the host writes a byte there and then waits for the
/// IN-ready flag (bit 7, active low) to clear before reading the 0xFE acknowledgement from
/// the data port (<see cref="Midi.DataPort"/>). A-TRAIN uses the "acknowledge every byte"
/// variant, so every byte sent on the command port - reset, UART and MIDI data alike -
/// must be acknowledged, or the host blocks forever waiting for the 0xFE.
/// </summary>
public sealed class MidiTest {
    /// <summary>A reset command (0xFF) sent on the command port clears the pending
    /// acknowledgements and is itself acknowledged: the IN-ready flag (bit 7, active low)
    /// reads 0 and the data port returns the 0xFE acknowledgement.</summary>
    [Fact]
    public void ResetCommandSentToStatusPort_IsAcknowledged() {
        // Arrange
        using MidiFixture fixture = new();

        // Act
        fixture.Midi.WriteByte(Midi.StatusPort, Midi.ResetCommand);

        // Assert
        fixture.Midi.State.Should().Be(GeneralMidiState.NormalMode);
        fixture.Midi.ReadByte(Midi.StatusPort).Should().Be(0x00);
        fixture.Midi.ReadByte(Midi.DataPort).Should().Be(Midi.CommandAcknowledge);
    }

    /// <summary>A UART-mode command (0x3F) sent on the command port switches the device to
    /// UART mode and is acknowledged with 0xFE on the data port.</summary>
    [Fact]
    public void UartModeCommandSentToStatusPort_SwitchesToUartModeAndIsAcknowledged() {
        // Arrange
        using MidiFixture fixture = new();

        // Act
        fixture.Midi.WriteByte(Midi.StatusPort, Midi.EnterUartModeCommand);

        // Assert
        fixture.Midi.State.Should().Be(GeneralMidiState.UartMode);
        fixture.Midi.ReadByte(Midi.StatusPort).Should().Be(0x00);
        fixture.Midi.ReadByte(Midi.DataPort).Should().Be(Midi.CommandAcknowledge);
    }

    /// <summary>A MIDI data byte (e.g. note-off 0x8A) sent on the command port must be
    /// acknowledged like any other byte; without it the host (A-TRAIN) blocks on the first
    /// note of its startup jingle.</summary>
    [Fact]
    public void MidiDataByteSentToStatusPort_IsAcknowledged() {
        // Arrange
        using MidiFixture fixture = new();

        // Act
        fixture.Midi.WriteByte(Midi.StatusPort, 0x8A);

        // Assert
        fixture.Midi.ReadByte(Midi.StatusPort).Should().Be(0x00);
        fixture.Midi.ReadByte(Midi.DataPort).Should().Be(Midi.CommandAcknowledge);
    }

    /// <summary>Each acknowledgement is consumed by a single data-port read: a second read
    /// on an empty queue returns 0 rather than replaying the previous acknowledgement.</summary>
    [Fact]
    public void Acknowledgement_IsConsumedByAReset() {
        // Arrange
        using MidiFixture fixture = new();

        // Act
        fixture.Midi.WriteByte(Midi.StatusPort, Midi.ResetCommand);
        fixture.Midi.ReadByte(Midi.DataPort);

        // Assert
        fixture.Midi.ReadByte(Midi.StatusPort).Should().Be(0x80);
        fixture.Midi.ReadByte(Midi.DataPort).Should().Be(0x00);
    }

    private sealed class MidiFixture : IDisposable {
        public MidiFixture() {
            ILogger logger = Substitute.For<ILogger>();
            State state = new(CpuModel.ZET_86);
            Dispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), state, logger, false);
            Mixer = new SoftwareMixer(Audio.Filters.AudioEngine.Dummy, new PauseHandler(logger));
            Midi = new Midi(new Configuration(), Mixer, state, Dispatcher, null, false, logger);
        }

        public IOPortDispatcher Dispatcher { get; }
        public SoftwareMixer Mixer { get; }
        public Midi Midi { get; }

        public void Dispose() {
            Midi.Dispose();
            Mixer.Dispose();
        }
    }
}
