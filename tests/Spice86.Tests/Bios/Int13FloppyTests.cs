namespace Spice86.Tests.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.Tests.Dos.FileSystem;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// TDD tests for INT 13h floppy sector read/write (AH=0x01, 0x02, 0x03, 0x08, 0x15).
/// </summary>
public sealed class Int13FloppyTests {
    // 1.44 MB floppy geometry
    private const int SectorsPerTrack = 18;
    private const int Heads = 2;
    private const int BytesPerSector = 512;

    // Drive number for A:
    private const byte DriveA = 0x00;

    // INT 13h buffer: ES:BX
    private const ushort BufferSegment = 0x0500;
    private const ushort BufferOffset = 0x0000;

    private sealed class TestContext {
        public Memory Memory { get; }
        public State State { get; }
        public SystemBiosInt13Handler Handler { get; }
        public DosDriveManager DriveManager { get; }

        public TestContext(byte[] floppyImage) {
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints breakpoints = new();
            A20Gate a20Gate = new(enabled: false);
            Memory = new Memory(breakpoints, ram, a20Gate, initializeResetVector: false);
            State = new State(CpuModel.INTEL_80386);

            ILoggerService logger = Substitute.For<ILoggerService>();
            IFunctionHandlerProvider functionHandlerProvider = Substitute.For<IFunctionHandlerProvider>();
            Stack stack = new(Memory, State);

            DriveManager = new DosDriveManager(logger, null, null);
            DriveManager.MountFloppyImage('A', floppyImage, "test.img");

            Handler = new SystemBiosInt13Handler(Memory, functionHandlerProvider, stack, State, DriveManager, logger);

            State.ES = BufferSegment;
            State.BX = BufferOffset;
        }

        public uint BufferAddress => MemoryUtils.ToPhysicalAddress(BufferSegment, BufferOffset);

        /// <summary>Converts CHS to the LBA byte offset in the image.</summary>
        public static int ChsToByteOffset(int cylinder, int head, int sector) {
            int lba = (cylinder * Heads + head) * SectorsPerTrack + (sector - 1);
            return lba * BytesPerSector;
        }

        public void SetupChsRegisters(int cylinder, int head, int sector, byte driveNumber, byte sectorCount) {
            State.DL = driveNumber;
            State.CH = (byte)(cylinder & 0xFF);
            State.CL = (byte)((sector & 0x3F) | ((cylinder >> 2) & 0xC0));
            State.DH = (byte)head;
            State.AL = sectorCount;
        }
    }

    [Fact]
    public void GetStatus_Initially_ReturnsZero() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        TestContext ctx = new(image);
        ctx.State.AH = 0x01;
        ctx.State.DL = DriveA;

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().Be(0x00, "status should be no error");
        ctx.State.CarryFlag.Should().BeFalse();
    }

    [Fact]
    public void ReadSectors_BootSector_ReturnsCorrectData() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        // The boot sector starts with EB 3C 90 (jump instruction) in Fat12ImageBuilder
        TestContext ctx = new(image);
        ctx.State.AH = 0x02;
        ctx.SetupChsRegisters(cylinder: 0, head: 0, sector: 1, driveNumber: DriveA, sectorCount: 1);

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().Be(0x00, "read should succeed");
        ctx.State.AL.Should().Be(1, "one sector transferred");
        ctx.State.CarryFlag.Should().BeFalse();

        // The boot sector bytes should be copied to ES:BX
        ctx.Memory.UInt8[ctx.BufferAddress].Should().Be(0xEB);
        ctx.Memory.UInt8[ctx.BufferAddress + 1].Should().Be(0x3C);
        ctx.Memory.UInt8[ctx.BufferAddress + 2].Should().Be(0x90);
    }

    [Fact]
    public void ReadSectors_MultipleSectors_CopiesCorrectBytes() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        TestContext ctx = new(image);
        ctx.State.AH = 0x02;
        // Read 2 sectors starting at cylinder 0, head 0, sector 1
        ctx.SetupChsRegisters(cylinder: 0, head: 0, sector: 1, driveNumber: DriveA, sectorCount: 2);

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().Be(0x00);
        ctx.State.AL.Should().Be(2);
        ctx.State.CarryFlag.Should().BeFalse();

        // First sector starts with the jump
        ctx.Memory.UInt8[ctx.BufferAddress].Should().Be(0xEB);
        // Second sector starts at offset 512 in the buffer
        // It is the second sector of the image (FAT start) — byte at offset 512 is 0xF0 media descriptor
        ctx.Memory.UInt8[ctx.BufferAddress + BytesPerSector].Should().Be(0xF0);
    }

    [Fact]
    public void ReadSectors_SecondHead_UsesCorrectByteOffset() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        // Write a sentinel byte at the start of the second head's first track, sector 1
        int byteOffset = TestContext.ChsToByteOffset(cylinder: 0, head: 1, sector: 1);
        image[byteOffset] = 0xAB;

        TestContext ctx = new(image);
        ctx.State.AH = 0x02;
        ctx.SetupChsRegisters(cylinder: 0, head: 1, sector: 1, driveNumber: DriveA, sectorCount: 1);

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().Be(0x00);
        ctx.Memory.UInt8[ctx.BufferAddress].Should().Be(0xAB);
    }

    [Fact]
    public void WriteSectors_BootSector_PersistsData() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        TestContext ctx = new(image);

        // Place sentinel bytes in buffer
        ctx.Memory.UInt8[ctx.BufferAddress] = 0xDE;
        ctx.Memory.UInt8[ctx.BufferAddress + 1] = 0xAD;
        ctx.Memory.UInt8[ctx.BufferAddress + 2] = 0xBE;

        ctx.State.AH = 0x03;
        ctx.SetupChsRegisters(cylinder: 0, head: 0, sector: 1, driveNumber: DriveA, sectorCount: 1);

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().Be(0x00);
        ctx.State.AL.Should().Be(1);
        ctx.State.CarryFlag.Should().BeFalse();

        // Verify the image bytes were updated
        byte[] rawImage = ctx.DriveManager.FloppyDrives['A'].GetCurrentImageData()!;
        rawImage[0].Should().Be(0xDE);
        rawImage[1].Should().Be(0xAD);
        rawImage[2].Should().Be(0xBE);
    }

    [Fact]
    public void GetDriveParameters_FloppyA_ReturnsCorrectGeometry() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        TestContext ctx = new(image);
        ctx.State.AH = 0x08;
        ctx.State.DL = DriveA;

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().Be(0x00);
        ctx.State.CarryFlag.Should().BeFalse();

        // DH = max head (0-based, so Heads-1)
        ctx.State.DH.Should().Be((byte)(Heads - 1));

        // CL bits 0-5 = max sector, CH = max cylinder (0-based, so 79 for 80 cylinders)
        int maxSector = ctx.State.CL & 0x3F;
        maxSector.Should().Be(SectorsPerTrack, "18 sectors per track");

        ctx.State.CH.Should().Be(79, "79 = last cylinder index for 80 cylinders");

        // BL = drive type: 4 = 3.5" 1.44 MB
        ctx.State.BL.Should().Be(4);
    }

    [Fact]
    public void GetDriveType_FloppyA_ReturnsDrivePresent() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        TestContext ctx = new(image);
        ctx.State.AH = 0x15;
        ctx.State.DL = DriveA;

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().BeGreaterThan(0x00, "floppy should be type 1 or 2, not 'not present'");
        ctx.State.CarryFlag.Should().BeFalse();
    }

    [Fact]
    public void ReadSectors_NoDriveImage_ReturnsError() {
        // Arrange — no floppy image mounted
        byte[] image = new Fat12ImageBuilder().Build();
        TestContext ctx = new(image);
        ctx.State.AH = 0x02;
        ctx.SetupChsRegisters(cylinder: 0, head: 0, sector: 1, driveNumber: 0x01 /* drive B, not mounted */, sectorCount: 1);

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.CarryFlag.Should().BeTrue("drive B is not mounted, should report error");
        ctx.State.AH.Should().NotBe(0x00);
    }

    [Fact]
    public void GetStatus_AfterFailedRead_ReturnsLastError() {
        // Arrange — attempt read on unmounted drive B, which will set error code
        byte[] image = new Fat12ImageBuilder().Build();
        TestContext ctx = new(image);
        ctx.State.AH = 0x02;
        ctx.SetupChsRegisters(cylinder: 0, head: 0, sector: 1, driveNumber: 0x01, sectorCount: 1);
        ctx.Handler.Run(); // This sets an error

        // Now ask for status
        ctx.State.AH = 0x01;
        ctx.State.DL = 0x01;

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().NotBe(0x00, "last error code should be non-zero after failed read");
    }
}
