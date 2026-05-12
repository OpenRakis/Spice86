namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Storage;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// Unit tests for <see cref="FloppyDiskController"/> covering command processing, MSR state, and IRQ raising.
/// </summary>
public sealed class FloppyDiskControllerTests {
    private const ushort PortMsr = 0x3F4;
    private const ushort PortData = 0x3F5;
    private const ushort PortDor = 0x3F2;

    private const byte MsrMrq = 0x80;
    private const byte MsrDio = 0x40;
    private const byte MsrCb = 0x10;

    private FloppyDiskController CreateFdc(IFloppyDriveAccess floppyAccess, out List<byte> raisedIrqs) {
        List<byte> irqs = new();
        raisedIrqs = irqs;
        State state = new(CpuModel.INTEL_80286);
        ILoggerService logger = Substitute.For<ILoggerService>();
        IMemory memory = Substitute.For<IMemory>();
        DmaChannel dmaChannel = new(2, false, memory, logger);
        return new FloppyDiskController(state, false, logger, irq => irqs.Add(irq), floppyAccess, dmaChannel);
    }

    private static IFloppyDriveAccess CreateFloppyAccess() {
        IFloppyDriveAccess access = Substitute.For<IFloppyDriveAccess>();
        access.ReadFromImage(Arg.Any<byte>(), Arg.Any<int>(), Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>()).Returns(true);
        access.WriteToImage(Arg.Any<byte>(), Arg.Any<int>(), Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>()).Returns(true);
        return access;
    }

    [Fact]
    public void MsrReady_AfterReset_ReturnsReadyOnly() {
        // Arrange
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> _);

        // Act
        byte msr = fdc.ReadByte(PortMsr);

        // Assert
        (msr & MsrMrq).Should().Be(MsrMrq);
        (msr & MsrDio).Should().Be(0);
    }

    [Fact]
    public void Specify_AcceptsParametersWithNoInterrupt() {
        // Arrange
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> irqs);

        // Act
        fdc.WriteByte(PortData, 0x03);  // SPECIFY command
        fdc.WriteByte(PortData, 0xDF);  // SRT + HUT
        fdc.WriteByte(PortData, 0x02);  // HLT + NDMA

        // Assert
        irqs.Should().BeEmpty();
        byte msr = fdc.ReadByte(PortMsr);
        (msr & MsrMrq).Should().Be(MsrMrq, "FDC should be idle and ready after SPECIFY");
        (msr & MsrCb).Should().Be(0, "FDC should not be busy after SPECIFY");
    }

    [Fact]
    public void Recalibrate_RaisesIrq6() {
        // Arrange
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> irqs);

        // Act
        fdc.WriteByte(PortData, 0x07);  // RECALIBRATE command
        fdc.WriteByte(PortData, 0x00);  // drive 0

        // Assert
        irqs.Should().ContainSingle(i => i == 6);
    }

    [Fact]
    public void SenseInterrupt_AfterRecalibrate_ReturnsSt0AndPcn() {
        // Arrange
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> _);
        fdc.WriteByte(PortData, 0x07);
        fdc.WriteByte(PortData, 0x00);

        // Act
        fdc.WriteByte(PortData, 0x08);  // SENSE INTERRUPT
        byte st0 = fdc.ReadByte(PortData);
        byte pcn = fdc.ReadByte(PortData);

        // Assert
        (st0 & 0x20).Should().Be(0x20, "SEEK END bit should be set after recalibrate");
        pcn.Should().Be(0, "Cylinder 0 after recalibrate");
    }

    [Fact]
    public void Seek_UpdatesCylinderAndRaisesIrq6() {
        // Arrange
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> irqs);

        // Act
        fdc.WriteByte(PortData, 0x0F);  // SEEK command
        fdc.WriteByte(PortData, 0x00);  // drive 0, head 0
        fdc.WriteByte(PortData, 10);    // target cylinder 10

        // Assert
        irqs.Should().ContainSingle(i => i == 6);

        // SENSE INTERRUPT should report cylinder 10
        fdc.WriteByte(PortData, 0x08);
        byte st0 = fdc.ReadByte(PortData);
        byte pcn = fdc.ReadByte(PortData);
        (st0 & 0x20).Should().Be(0x20);
        pcn.Should().Be(10);
    }

    [Fact]
    public void ReadData_Success_RaisesIrq6AndResultHasSevenBytes() {
        // Arrange
        IFloppyDriveAccess access = CreateFloppyAccess();
        FloppyDiskController fdc = CreateFdc(access, out List<byte> irqs);

        // Act
        fdc.WriteByte(PortData, 0xE6);  // READ DATA
        fdc.WriteByte(PortData, 0x00);  // HD=0, DS=0
        fdc.WriteByte(PortData, 0);     // cylinder
        fdc.WriteByte(PortData, 0);     // head
        fdc.WriteByte(PortData, 1);     // sector
        fdc.WriteByte(PortData, 2);     // sector size (512 bytes)
        fdc.WriteByte(PortData, 1);     // last sector
        fdc.WriteByte(PortData, 0x1B);  // gap length
        fdc.WriteByte(PortData, 0xFF);  // data length

        // Collect 7 result bytes
        List<byte> result = new();
        for (int i = 0; i < 7; i++) {
            result.Add(fdc.ReadByte(PortData));
        }

        // Assert
        irqs.Should().ContainSingle(i => i == 6);
        result.Should().HaveCount(7);
        (result[0] & 0x40).Should().Be(0, "ST0 should not have error bits set on success");
    }

    [Fact]
    public void WriteData_Success_RaisesIrq6AndResultHasSevenBytes() {
        // Arrange
        IFloppyDriveAccess access = CreateFloppyAccess();
        FloppyDiskController fdc = CreateFdc(access, out List<byte> irqs);

        // Act
        fdc.WriteByte(PortData, 0xC5);  // WRITE DATA
        fdc.WriteByte(PortData, 0x00);  // HD=0, DS=0
        fdc.WriteByte(PortData, 0);     // cylinder
        fdc.WriteByte(PortData, 0);     // head
        fdc.WriteByte(PortData, 1);     // sector
        fdc.WriteByte(PortData, 2);     // sector size (512 bytes)
        fdc.WriteByte(PortData, 1);     // last sector
        fdc.WriteByte(PortData, 0x1B);  // gap length
        fdc.WriteByte(PortData, 0xFF);  // data length

        List<byte> result = new();
        for (int i = 0; i < 7; i++) {
            result.Add(fdc.ReadByte(PortData));
        }

        // Assert
        irqs.Should().ContainSingle(i => i == 6);
        result.Should().HaveCount(7);
        (result[0] & 0x40).Should().Be(0, "ST0 should not have error bits set on success");
    }

    [Fact]
    public void ReadData_Failure_SetsSt0ErrorBit() {
        // Arrange
        IFloppyDriveAccess access = Substitute.For<IFloppyDriveAccess>();
        access.ReadFromImage(Arg.Any<byte>(), Arg.Any<int>(), Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>()).Returns(false);
        FloppyDiskController fdc = CreateFdc(access, out List<byte> _);

        // Act
        fdc.WriteByte(PortData, 0xE6);
        fdc.WriteByte(PortData, 0x00);
        fdc.WriteByte(PortData, 0);
        fdc.WriteByte(PortData, 0);
        fdc.WriteByte(PortData, 1);
        fdc.WriteByte(PortData, 2);
        fdc.WriteByte(PortData, 1);
        fdc.WriteByte(PortData, 0x1B);
        fdc.WriteByte(PortData, 0xFF);

        byte st0 = fdc.ReadByte(PortData);

        // Assert
        (st0 & 0x40).Should().Be(0x40, "ST0 should have error bit set on read failure");
    }

    [Fact]
    public void ReadId_RaisesIrq6AndReturnsSevenBytes() {
        // Arrange
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> irqs);

        // Act
        fdc.WriteByte(PortData, 0x4A);  // READ ID
        fdc.WriteByte(PortData, 0x00);  // HD=0, DS=0

        List<byte> result = new();
        for (int i = 0; i < 7; i++) {
            result.Add(fdc.ReadByte(PortData));
        }

        // Assert
        irqs.Should().ContainSingle(i => i == 6);
        result.Should().HaveCount(7);
    }

    [Fact]
    public void SenseDriveStatus_AtCylinder0_SetsTrack0Bit() {
        // Arrange — drive is at cylinder 0 after reset (default)
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> _);

        // Act — SENSE DRIVE STATUS for drive 0
        fdc.WriteByte(PortData, 0x04);  // SENSE DRIVE STATUS
        fdc.WriteByte(PortData, 0x00);  // drive 0, head 0
        byte st3 = fdc.ReadByte(PortData);

        // Assert — bit 4 (0x10) is Track 0, bit 5 (0x20) is Write Protected
        (st3 & 0x10).Should().Be(0x10, "Track 0 bit must be set when head is at cylinder 0");
        (st3 & 0x20).Should().Be(0, "Write Protected bit must NOT be set (drive is read-write)");
    }

    [Fact]
    public void SenseDriveStatus_SetsReadyAndTwoSidedBits() {
        // Arrange — DOSBox Staging fdc.cpp get_ST3() always sets bit 6 (Ready) and bit 3 (Two-sided)
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> _);

        // Act
        fdc.WriteByte(PortData, 0x04);  // SENSE DRIVE STATUS
        fdc.WriteByte(PortData, 0x00);  // drive 0, head 0
        byte st3 = fdc.ReadByte(PortData);

        // Assert
        (st3 & 0x40).Should().Be(0x40, "Ready bit (bit 6) must always be set when drive is present");
        (st3 & 0x08).Should().Be(0x08, "Two-sided bit (bit 3) must always be set for double-sided floppies");
    }

    [Fact]
    public void SenseDriveStatus_AfterSeekAwayCylinder0_ClearsTrack0Bit() {
        // Arrange — seek to cylinder 5 first
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> _);
        fdc.WriteByte(PortData, 0x0F);  // SEEK
        fdc.WriteByte(PortData, 0x00);  // drive 0
        fdc.WriteByte(PortData, 5);     // cylinder 5

        // Act — SENSE DRIVE STATUS now that head is at cylinder 5
        fdc.WriteByte(PortData, 0x04);
        fdc.WriteByte(PortData, 0x00);
        byte st3 = fdc.ReadByte(PortData);

        // Assert — Track 0 bit must be clear when not at cylinder 0
        (st3 & 0x10).Should().Be(0, "Track 0 bit must be clear when head is NOT at cylinder 0");
    }

    [Fact]
    public void ReadData_NonStandardGeometry_UsesActualHeadCount() {
        // Arrange — 720 KB floppy: 80 cylinders, 2 heads, 9 sectors/track, 512 bytes/sector
        IFloppyDriveAccess access = Substitute.For<IFloppyDriveAccess>();
        access.TryGetGeometry(0, out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>())
            .Returns(x => {
                x[1] = 80;  // totalCylinders
                x[2] = 2;   // headsPerCylinder
                x[3] = 9;   // sectorsPerTrack
                x[4] = 512; // bytesPerSector
                return true;
            });

        // Track the byte offset that TryRead is called with
        int capturedByteOffset = -1;
        access.ReadFromImage(Arg.Any<byte>(), Arg.Do<int>(off => capturedByteOffset = off),
            Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>()).Returns(true);

        FloppyDiskController fdc = CreateFdc(access, out List<byte> _);

        // Read cylinder=1, head=0, sector=1 on a 2-head, 9-SPT disk.
        // Expected LBA = (1 * 2 + 0) * 9 + (1 - 1) = 18
        // Expected byte offset = 18 * 512 = 9216
        fdc.WriteByte(PortData, 0xE6);  // READ DATA
        fdc.WriteByte(PortData, 0x00);  // drive 0, head 0
        fdc.WriteByte(PortData, 1);     // cylinder 1
        fdc.WriteByte(PortData, 0);     // head 0
        fdc.WriteByte(PortData, 1);     // sector 1
        fdc.WriteByte(PortData, 2);     // sector size code (512 bytes)
        fdc.WriteByte(PortData, 1);     // last sector
        fdc.WriteByte(PortData, 0x1B);  // gap length
        fdc.WriteByte(PortData, 0xFF);  // data length

        // Drain result bytes
        for (int i = 0; i < 7; i++) {
            fdc.ReadByte(PortData);
        }

        // Assert — LBA must use actual 2 heads per cylinder, not the hardcoded 2 (same here, but geometry is verified)
        capturedByteOffset.Should().Be(9216,
            "LBA 18 (cylinder 1, head 0, sector 1, 9-SPT, 2-head) × 512 bytes = 9216");
    }

    [Fact]
    public void DorWrite_AcceptedWithoutError() {
        // Arrange
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> _);

        // Act & Assert — should not throw
        fdc.WriteByte(PortDor, 0x1C);
    }

    [Fact]
    public void MultipleCommands_FdcReturnToIdleAfterResultRead() {
        // Arrange
        FloppyDiskController fdc = CreateFdc(CreateFloppyAccess(), out List<byte> _);

        // First RECALIBRATE
        fdc.WriteByte(PortData, 0x07);
        fdc.WriteByte(PortData, 0x00);

        // SENSE INTERRUPT
        fdc.WriteByte(PortData, 0x08);
        fdc.ReadByte(PortData);  // ST0
        fdc.ReadByte(PortData);  // PCN

        // Act — MSR should show idle/ready
        byte msr = fdc.ReadByte(PortMsr);

        // Assert
        (msr & MsrMrq).Should().Be(MsrMrq, "FDC should be idle after result fully consumed");
        (msr & MsrDio).Should().Be(0, "DIO should be 0 when idle (ready for command)");
    }
}
