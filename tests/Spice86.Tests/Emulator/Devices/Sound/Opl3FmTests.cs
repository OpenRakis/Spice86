namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.CLI.RuntimeOptions;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;

using Xunit;

public sealed class Opl3FmTests {
    /// <summary>A timer register pair behaves the same whether it is written through the address
    /// and data ports or through <see cref="Opl3Fm.WriteRegister"/>: the 80us timer overflows on
    /// the same clock, and a data-only write to the still-selected control register resets it.
    /// The cases cover the first OPL3 bank, both DualOpl2 chips and a non-default Sound Blaster
    /// base.</summary>
    [Theory]
    [InlineData(OplMode.Opl3, 0x220, 0x388)]
    [InlineData(OplMode.Opl3, 0x220, 0x228)]
    [InlineData(OplMode.Opl3, 0x240, 0x240)]
    [InlineData(OplMode.DualOpl2, 0x220, 0x220)]
    [InlineData(OplMode.DualOpl2, 0x220, 0x222)]
    [InlineData(OplMode.DualOpl2, 0x220, 0x38A)]
    public void WriteRegisterMatchesThePortSequenceAndRetainsTheAddress(
        OplMode oplMode, int sbBase, int addressPort) {
        // Arrange: two independent chips on the same frozen clock, one driven through its ports,
        // one through the operation.
        using OplFixture viaPorts = new(oplMode, sbBase);
        using OplFixture viaOperation = new(oplMode, sbBase);
        ushort address = (ushort)addressPort;
        ushort data = (ushort)(addressPort + 1);

        // Act: load the timer0 counter (register 0x02) then start it unmasked through the
        // control register (0x04).
        viaPorts.Dispatcher.WriteByte(address, 0x02);
        viaPorts.Dispatcher.WriteByte(data, 0xFF);
        viaPorts.Dispatcher.WriteByte(address, 0x04);
        viaPorts.Dispatcher.WriteByte(data, 0x01);
        viaOperation.Opl.WriteRegister(address, 0x02, 0xFF);
        viaOperation.Opl.WriteRegister(address, 0x04, 0x01);
        viaPorts.Clock.ElapsedTimeMs.Returns(1.0);
        viaOperation.Clock.ElapsedTimeMs.Returns(1.0);

        // Assert: the 80us timer overflowed in both devices.
        byte expectedStatus = viaPorts.Dispatcher.ReadByte(address);
        byte actualStatus = viaOperation.Dispatcher.ReadByte(address);
        actualStatus.Should().Be(expectedStatus);
        (actualStatus & 0xC0).Should().Be(0xC0);

        // Act: a data-only write to the still-selected control register resets the interrupt
        // flag - it only reaches register 0x04 if the address latch was kept.
        viaPorts.Dispatcher.WriteByte(data, 0x80);
        viaOperation.Dispatcher.WriteByte(data, 0x80);

        // Assert: the status flags cleared identically on both devices.
        actualStatus = viaOperation.Dispatcher.ReadByte(address);
        actualStatus.Should().Be(viaPorts.Dispatcher.ReadByte(address));
        (actualStatus & 0xC0).Should().Be(0);
    }

    private sealed class OplFixture : IDisposable {
        public OplFixture(OplMode oplMode, int sbBase) {
            ILogger logger = Substitute.For<ILogger>();
            State state = new(CpuModel.ZET_86);
            Dispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), state, logger, false);
            Clock = Substitute.For<IEmulatedClock>();
            Mixer = new SoftwareMixer(Audio.Filters.AudioEngine.Dummy, new PauseHandler(logger));
            AudioRuntimeOptions options = new(Audio.Filters.AudioEngine.Dummy, null,
                oplMode, (ushort)sbBase, false, 7, 1, 5, SbType.SBPro2);
            Opl = new Opl3Fm(options, Mixer, state, Clock, Dispatcher, false, logger);
        }

        public IEmulatedClock Clock { get; }
        public SoftwareMixer Mixer { get; }
        public IOPortDispatcher Dispatcher { get; }
        public Opl3Fm Opl { get; }

        public void Dispose() {
            Opl.Dispose();
            Mixer.Dispose();
        }
    }
}
