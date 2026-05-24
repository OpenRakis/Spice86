namespace Spice86.Tests.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

public class SystemBiosInt13HandlerTests {
    [Theory]
    [InlineData(40, 2, 9, 512, 0x01)]
    [InlineData(80, 2, 9, 512, 0x03)]
    [InlineData(80, 2, 15, 512, 0x02)]
    [InlineData(80, 2, 18, 512, 0x04)]
    [InlineData(80, 2, 36, 512, 0x06)]
    public void GetDriveParameters_ReturnsDosBoxBiosTypeForMountedFloppies(int totalCylinders,
        int headsPerCylinder, int sectorsPerTrack, int bytesPerSector, byte expectedBiosType) {
        // Arrange
        State state = new(CpuModel.INTEL_80286) {
            DL = 0,
        };
        A20Gate a20Gate = new(false);
        Memory memory = new(new(), new Ram(A20Gate.EndOfHighMemoryArea), a20Gate, new RealModeMmu386(), false);
        Stack stack = new(memory, state);
        IFunctionHandlerProvider functionHandlerProvider = Substitute.For<IFunctionHandlerProvider>();
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        using EmulatedClock clock = new(null, DateTimeOffset.UnixEpoch);
        DeviceScheduler scheduler = new(clock, loggerService, "test-floppy-scheduler");
        FloppyDiskTimingService timingService = new(state, clock, scheduler, FloppyDiskSpeed.Maximum);
        TestFloppyDriveAccess floppyAccess = new(totalCylinders, headsPerCylinder, sectorsPerTrack, bytesPerSector);
        SystemBiosInt13Handler handler = new(memory, functionHandlerProvider, stack, state, floppyAccess,
            floppySound: null, activityNotifier: null, timingService, loggerService);

        // Act
        handler.GetDriveParameters(false);

        // Assert
        state.CarryFlag.Should().BeFalse();
        state.AH.Should().Be(0);
        state.BL.Should().Be(expectedBiosType,
            "DOSBox reports BIOS type from the mounted floppy geometry instead of hardcoding 1.44 MB");
    }

    private sealed class TestFloppyDriveAccess : IFloppyDriveAccess {
        private readonly int _totalCylinders;
        private readonly int _headsPerCylinder;
        private readonly int _sectorsPerTrack;
        private readonly int _bytesPerSector;

        public TestFloppyDriveAccess(int totalCylinders, int headsPerCylinder, int sectorsPerTrack,
            int bytesPerSector) {
            _totalCylinders = totalCylinders;
            _headsPerCylinder = headsPerCylinder;
            _sectorsPerTrack = sectorsPerTrack;
            _bytesPerSector = bytesPerSector;
        }

        public bool TryGetGeometry(byte driveNumber, out int totalCylinders, out int headsPerCylinder,
            out int sectorsPerTrack, out int bytesPerSector) {
            if (driveNumber != 0) {
                totalCylinders = 0;
                headsPerCylinder = 0;
                sectorsPerTrack = 0;
                bytesPerSector = 0;
                return false;
            }

            totalCylinders = _totalCylinders;
            headsPerCylinder = _headsPerCylinder;
            sectorsPerTrack = _sectorsPerTrack;
            bytesPerSector = _bytesPerSector;
            return true;
        }

        public bool ReadFromImage(byte driveNumber, int imageByteOffset, byte[] destination, int destOffset,
            int byteCount) {
            return false;
        }

        public bool WriteToImage(byte driveNumber, int imageByteOffset, byte[] source, int srcOffset,
            int byteCount) {
            return false;
        }
    }
}