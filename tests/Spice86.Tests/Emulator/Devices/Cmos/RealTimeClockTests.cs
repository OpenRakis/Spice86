namespace Spice86.Tests.Emulator.Devices.Cmos;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using Xunit;

public sealed class RealTimeClockTests {
    [Fact]
    public void ReadRegisterMatchesThePortSequenceAndKeepsTheSelection() {
        // Arrange: two independent chips on the same frozen time, one selects the register through
        // its ports, one through the operation. The address keeps its NMI-disable bit set.
        using RtcFixture viaPorts = new();
        using RtcFixture viaOperation = new();
        const byte address = 0x8B; // Status register B with the NMI-disable bit set

        // Act
        viaPorts.Dispatcher.WriteByte(0x70, address);
        byte expected = viaPorts.Dispatcher.ReadByte(0x71);
        byte actual = viaOperation.Clock.ReadRegister(address);

        // Assert: the values match, and a following data read without reselection reads the same
        // register in both.
        actual.Should().Be(expected);
        viaOperation.Dispatcher.ReadByte(0x71).Should().Be(viaPorts.Dispatcher.ReadByte(0x71));
    }

    [Fact]
    public void WriteRegisterMatchesThePortSequence() {
        // Arrange
        using RtcFixture viaPorts = new();
        using RtcFixture viaOperation = new();
        const byte address = 0xA0; // CMOS RAM register with the NMI-disable bit set

        // Act
        viaPorts.Dispatcher.WriteByte(0x70, address);
        viaPorts.Dispatcher.WriteByte(0x71, 0x45);
        viaOperation.Clock.WriteRegister(address, 0x45);

        // Assert: the write landed on the same register in both, and the selection was kept.
        viaOperation.Dispatcher.ReadByte(0x71).Should().Be(viaPorts.Dispatcher.ReadByte(0x71));
        viaPorts.Dispatcher.WriteByte(0x70, address);
        viaOperation.Dispatcher.WriteByte(0x70, address);
        viaOperation.Dispatcher.ReadByte(0x71).Should().Be(viaPorts.Dispatcher.ReadByte(0x71));
    }

    private sealed class RtcFixture : IDisposable {
        public RtcFixture() {
            ILogger logger = Substitute.For<ILogger>();
            State state = new(CpuModel.ZET_86);
            AddressReadWriteBreakpoints breakpoints = new();
            Dispatcher = new IOPortDispatcher(breakpoints, state, logger, false);
            DualPic dualPic = new(Dispatcher, state, logger, false);
            IEmulatedClock emulatedClock = Substitute.For<IEmulatedClock>();
            DeviceScheduler emulationLoopScheduler = new(emulatedClock, logger, "Emulation loop");
            Clock = new RealTimeClock(state, Dispatcher, dualPic, emulationLoopScheduler,
                emulatedClock, false, logger);
        }

        public IOPortDispatcher Dispatcher { get; }
        public RealTimeClock Clock { get; }

        public void Dispose() => Clock.Dispose();
    }
}
