namespace Spice86.Tests.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Storage;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.Tests.Dos.FileSystem;

using Xunit;

/// <summary>
/// TDD tests for INT 13h floppy sector read/write (AH=0x01, 0x02, 0x03, 0x08, 0x15).
/// </summary>
public sealed class Int13FloppyTests {
    // 1.44 MB floppy geometry (matches Fat12ImageBuilder defaults)
    private const int SectorsPerTrack = 18;
    private const int Heads = 2;
    private const int BytesPerSector = 512;

    // Drive A: = BIOS drive number 0
    private const byte DriveA = 0x00;

    // INT 13h transfer buffer: ES:BX
    private const ushort BufferSegment = 0x0500;
    private const ushort BufferOffset = 0x0000;

    private sealed class TestContext {
        public Memory Memory { get; }
        public State State { get; }
        public SystemBiosInt13Handler Handler { get; }
        public IFloppyDriveAccess FloppyAccess { get; }

        public TestContext(byte[] floppyImage) {
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints breakpoints = new();
            A20Gate a20Gate = new(enabled: false);
            Memory = new Memory(breakpoints, ram, a20Gate, initializeResetVector: false);
            State = new State(CpuModel.INTEL_80386);

            ILoggerService logger = Substitute.For<ILoggerService>();
            Stack stack = new(Memory, State);

            FunctionHandler functionHandler = new(Memory, State, new FunctionCatalogue(), false, logger);
            IFunctionHandlerProvider functionHandlerProvider = Substitute.For<IFunctionHandlerProvider>();
            functionHandlerProvider.FunctionHandlerInUse.Returns(functionHandler);

            DosDriveManager driveManager = new(logger, null, null);
            driveManager.MountFloppyImage('A', floppyImage, "test.img");
            FloppyAccess = driveManager;

            Handler = new SystemBiosInt13Handler(Memory, functionHandlerProvider, stack, State, FloppyAccess, logger);

            State.ES = BufferSegment;
            State.BX = BufferOffset;
        }

        public uint BufferAddress => MemoryUtils.ToPhysicalAddress(BufferSegment, BufferOffset);

        /// <summary>Converts CHS to the byte offset in a flat 1.44 MB floppy image.</summary>
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
        ctx.State.AH.Should().Be(0x00, "no previous operation, so status should be no error");
        ctx.State.CarryFlag.Should().BeFalse();
    }

    [Fact]
    public void ReadSectors_BootSector_ReturnsCorrectData() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        // The boot sector starts with EB 3C 90 (jump instruction written by Fat12ImageBuilder)
        TestContext ctx = new(image);
        ctx.State.AH = 0x02;
        ctx.SetupChsRegisters(cylinder: 0, head: 0, sector: 1, driveNumber: DriveA, sectorCount: 1);

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().Be(0x00, "read should succeed");
        ctx.State.AL.Should().Be(1, "one sector transferred");
        ctx.State.CarryFlag.Should().BeFalse();
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
        ctx.SetupChsRegisters(cylinder: 0, head: 0, sector: 1, driveNumber: DriveA, sectorCount: 2);

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().Be(0x00);
        ctx.State.AL.Should().Be(2);
        ctx.State.CarryFlag.Should().BeFalse();
        // First sector: jump instruction
        ctx.Memory.UInt8[ctx.BufferAddress].Should().Be(0xEB);
        // Second sector: FAT starts with media descriptor 0xF0
        ctx.Memory.UInt8[ctx.BufferAddress + BytesPerSector].Should().Be(0xF0);
    }

    [Fact]
    public void ReadSectors_SecondHead_UsesCorrectByteOffset() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
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

        // Verify the image bytes were updated through IFloppyDriveAccess
        byte[] readback = new byte[3];
        ctx.FloppyAccess.TryRead(DriveA, 0, readback, 0, 3).Should().BeTrue();
        readback[0].Should().Be(0xDE);
        readback[1].Should().Be(0xAD);
        readback[2].Should().Be(0xBE);
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
        ctx.State.DH.Should().Be((byte)(Heads - 1), "DH = max head (0-based)");
        int maxSector = ctx.State.CL & 0x3F;
        maxSector.Should().Be(SectorsPerTrack, "CL bits 0-5 = sectors per track");
        ctx.State.CH.Should().Be(79, "CH = max cylinder index (80 cylinders → index 79)");
        ctx.State.BL.Should().Be(4, "BL = 4 means 3.5\" 1.44 MB");
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
        ctx.State.AH.Should().BeGreaterThan(0x00, "a mounted floppy should not report 'not present'");
        ctx.State.CarryFlag.Should().BeFalse();
    }

    [Fact]
    public void ReadSectors_NoDriveImage_ReturnsError() {
        // Arrange — drive B (0x01) is not mounted
        byte[] image = new Fat12ImageBuilder().Build();
        TestContext ctx = new(image);
        ctx.State.AH = 0x02;
        ctx.SetupChsRegisters(cylinder: 0, head: 0, sector: 1, driveNumber: 0x01, sectorCount: 1);

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.CarryFlag.Should().BeTrue("drive B is not mounted");
        ctx.State.AH.Should().NotBe(0x00);
    }

    [Fact]
    public void GetStatus_AfterFailedRead_ReturnsLastError() {
        // Arrange — fail a read on unmounted drive B first
        byte[] image = new Fat12ImageBuilder().Build();
        TestContext ctx = new(image);
        ctx.State.AH = 0x02;
        ctx.SetupChsRegisters(cylinder: 0, head: 0, sector: 1, driveNumber: 0x01, sectorCount: 1);
        ctx.Handler.Run();

        // Now query status for drive B
        ctx.State.AH = 0x01;
        ctx.State.DL = 0x01;

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.AH.Should().NotBe(0x00, "last error code should be non-zero after failed read");
    }
}
