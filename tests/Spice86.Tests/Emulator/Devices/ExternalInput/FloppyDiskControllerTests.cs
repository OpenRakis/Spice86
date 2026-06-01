namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Dos;

using System;

using Xunit;

public sealed class FloppyDiskControllerTests {
    [Fact]
    public void ReadDataCommand_TransfersSectorIntoDmaBuffer_AndRaisesIrq6() {
        // Arrange
        FloppyDiskControllerFixture fixture = new(FloppyDiskSpeed.Maximum);
        byte[] image = new byte[1440 * 1024];
        image[0] = 0xDE;
        image[1] = 0xAD;
        image[2] = 0xBE;
        image[3] = 0xEF;
        fixture.DriveManager.MountFloppyImage('A', image, "fdc-read.img");
        fixture.DualPic.SetIrqMask(6, false);
        fixture.ProgramChannel2Dma(0x2000, 512);

        // Act
        fixture.Dispatcher.WriteByte(0x3F5, 0xE6);
        fixture.Dispatcher.WriteByte(0x3F5, 0x00);
        fixture.Dispatcher.WriteByte(0x3F5, 0x00);
        fixture.Dispatcher.WriteByte(0x3F5, 0x00);
        fixture.Dispatcher.WriteByte(0x3F5, 0x01);
        fixture.Dispatcher.WriteByte(0x3F5, 0x02);
        fixture.Dispatcher.WriteByte(0x3F5, 0x01);
        fixture.Dispatcher.WriteByte(0x3F5, 0x1B);
        fixture.Dispatcher.WriteByte(0x3F5, 0xFF);

        // Assert
        fixture.Memory.UInt8[0x2000].Should().Be(0xDE);
        fixture.Memory.UInt8[0x2001].Should().Be(0xAD);
        fixture.Memory.UInt8[0x2002].Should().Be(0xBE);
        fixture.Memory.UInt8[0x2003].Should().Be(0xEF);
        fixture.DualPic.IrqCheck.Should().BeTrue();
        fixture.DualPic.ComputeVectorNumber().Should().Be(0x0E);
    }

    [Fact]
    public void WriteDataCommand_TransfersDmaBufferIntoFloppyImage_AndRaisesIrq6() {
        // Arrange
        FloppyDiskControllerFixture fixture = new(FloppyDiskSpeed.Maximum);
        byte[] image = new byte[1440 * 1024];
        fixture.DriveManager.MountFloppyImage('A', image, "fdc-write.img");
        fixture.DualPic.SetIrqMask(6, false);
        fixture.Memory.UInt8[0x3000] = 0x12;
        fixture.Memory.UInt8[0x3001] = 0x34;
        fixture.Memory.UInt8[0x3002] = 0x56;
        fixture.Memory.UInt8[0x3003] = 0x78;
        fixture.ProgramChannel2Dma(0x3000, 512);

        // Act
        fixture.Dispatcher.WriteByte(0x3F5, 0xC5);
        fixture.Dispatcher.WriteByte(0x3F5, 0x00);
        fixture.Dispatcher.WriteByte(0x3F5, 0x00);
        fixture.Dispatcher.WriteByte(0x3F5, 0x00);
        fixture.Dispatcher.WriteByte(0x3F5, 0x01);
        fixture.Dispatcher.WriteByte(0x3F5, 0x02);
        fixture.Dispatcher.WriteByte(0x3F5, 0x01);
        fixture.Dispatcher.WriteByte(0x3F5, 0x1B);
        fixture.Dispatcher.WriteByte(0x3F5, 0xFF);

        // Assert
        byte[] sectorBytes = new byte[4];
        bool readSuccess = fixture.DriveManager.ReadFromImage(0, 0, sectorBytes, 0, sectorBytes.Length);
        readSuccess.Should().BeTrue();
        sectorBytes.Should().Equal(0x12, 0x34, 0x56, 0x78);
        fixture.DualPic.IrqCheck.Should().BeTrue();
        fixture.DualPic.ComputeVectorNumber().Should().Be(0x0E);
    }

    [Fact]
    public void ReadDataCommand_WithFastTiming_AdvancesCycles() {
        // Arrange
        FloppyDiskControllerFixture fixture = new(FloppyDiskSpeed.Fast);
        byte[] image = new byte[1440 * 1024];
        image[0] = 0xAA;
        fixture.DriveManager.MountFloppyImage('A', image, "fdc-timing.img");
        fixture.DualPic.SetIrqMask(6, false);
        fixture.ProgramChannel2Dma(0x2000, 512);

        // Act
        fixture.Dispatcher.WriteByte(0x3F5, 0xE6);
        fixture.Dispatcher.WriteByte(0x3F5, 0x00);
        fixture.Dispatcher.WriteByte(0x3F5, 0x00);
        fixture.Dispatcher.WriteByte(0x3F5, 0x00);
        fixture.Dispatcher.WriteByte(0x3F5, 0x01);
        fixture.Dispatcher.WriteByte(0x3F5, 0x02);
        fixture.Dispatcher.WriteByte(0x3F5, 0x01);
        fixture.Dispatcher.WriteByte(0x3F5, 0x1B);
        fixture.Dispatcher.WriteByte(0x3F5, 0xFF);

        // Assert
        fixture.State.Cycles.Should().BeGreaterThan(0);
    }

    private sealed class FloppyDiskControllerFixture {
        public FloppyDiskControllerFixture(FloppyDiskSpeed speed) {
            Logger = Substitute.For<ILoggerService>();
            Logger.IsEnabled(Arg.Any<LogEventLevel>()).Returns(false);

            State = new State(CpuModel.INTEL_8086);
            AddressReadWriteBreakpoints ioBreakpoints = new();
            Dispatcher = new IOPortDispatcher(ioBreakpoints, State, Logger, false);

            AddressReadWriteBreakpoints memoryBreakpoints = new();
            Memory = new Memory(memoryBreakpoints, new Ram(0x200000), new A20Gate(), new RealModeMmu386(), false);
            DmaBus = new DmaBus(Memory, State, Dispatcher, false, Logger);
            DualPic = new DualPic(Dispatcher, State, Logger, false);
            DriveManager = DosTestHelpers.CreateDriveManager(Logger, null);

            IDriveActivityNotifier activityNotifier = Substitute.For<IDriveActivityNotifier>();
            DmaChannel channel2 = DmaBus.GetChannel(2)
                ?? throw new InvalidOperationException("DMA channel 2 unavailable for floppy controller test.");
            CyclesClock clock = new(State, 1000, null, DateTimeOffset.UnixEpoch);
            DeviceScheduler scheduler = new(clock, Logger, "Floppy timing test");
            FloppyDiskTimingService timingService = new(State, clock, scheduler, speed);
            FloppyDiskTransferService transferService = new(DriveManager, channel2, activityNotifier, timingService);
            Controller = new FloppyDiskController(State, Dispatcher, false, Logger, DualPic, transferService);
        }

        public ILoggerService Logger { get; }
        public State State { get; }
        public IOPortDispatcher Dispatcher { get; }
        public Memory Memory { get; }
        public DmaBus DmaBus { get; }
        public DualPic DualPic { get; }
        public DosDriveManager DriveManager { get; }
        public FloppyDiskController Controller { get; }

        public void ProgramChannel2Dma(uint address, ushort transferLength) {
            ushort transferCount = (ushort)(transferLength - 1);

            Dispatcher.WriteByte(0x0C, 0x00);
            Dispatcher.WriteByte(0x04, (byte)(address & 0xFF));
            Dispatcher.WriteByte(0x04, (byte)((address >> 8) & 0xFF));
            Dispatcher.WriteByte(0x05, (byte)(transferCount & 0xFF));
            Dispatcher.WriteByte(0x05, (byte)((transferCount >> 8) & 0xFF));
            Dispatcher.WriteByte(0x81, (byte)((address >> 16) & 0xFF));
            Dispatcher.WriteByte(0x0A, 0x02);
        }
    }
}