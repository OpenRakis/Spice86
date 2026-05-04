namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.Tests.Dos.FileSystem;

using Xunit;

/// <summary>TDD tests for INT 25h Absolute Disk Read.</summary>
public sealed class AbsoluteDiskReadTests {
    private const ushort BufferSegment = 0x0500;
    private const ushort BufferOffset = 0x0000;
    private const int BytesPerSector = 512;

    private sealed class Ctx {
        public Memory Memory { get; }
        public State State { get; }
        public DosDiskInt25Handler Handler { get; }
        public DosDriveManager DriveManager { get; }

        public Ctx(byte[] image) {
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints breakpoints = new();
            A20Gate a20 = new(enabled: false);
            Memory = new Memory(breakpoints, ram, a20, initializeResetVector: false);
            State = new State(CpuModel.INTEL_80386);
            ILoggerService logger = Substitute.For<ILoggerService>();
            Stack stack = new(Memory, State);
            FunctionHandler fh = new(Memory, State, new FunctionCatalogue(), false, logger);
            IFunctionHandlerProvider fhp = Substitute.For<IFunctionHandlerProvider>();
            fhp.FunctionHandlerInUse.Returns(fh);

            DriveManager = DosTestHelpers.CreateDriveManager(logger, null);
            DriveManager.MountFloppyImage('A', image, "test.img");

            Handler = new DosDiskInt25Handler(Memory, DriveManager, fhp, stack, State, logger);

            State.DS = BufferSegment;
            State.BX = BufferOffset;
        }

        public uint BufferAddress => MemoryUtils.ToPhysicalAddress(BufferSegment, BufferOffset);
    }

    [Fact]
    public void ReadSector0_FloppyA_ReturnsBpbData() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        Ctx ctx = new(image);
        ctx.State.AL = 0x00; // drive A
        ctx.State.CX = 1;    // 1 sector
        ctx.State.DX = 0;    // logical sector 0

        // Act
        ctx.Handler.Run();

        // Assert
        ctx.State.CarryFlag.Should().BeFalse();
        ctx.Memory.UInt8[ctx.BufferAddress].Should().Be(0xEB, "BPB jump instruction byte 0");
        ctx.Memory.UInt8[ctx.BufferAddress + 1].Should().Be(0x3C, "BPB jump instruction byte 1");
    }

    [Fact]
    public void ReadMultipleSectors_FloppyA_CopiesToBuffer() {
        byte[] image = new Fat12ImageBuilder().Build();
        // Place a unique byte at start of sector 1
        image[BytesPerSector] = 0xBB;
        Ctx ctx = new(image);
        ctx.State.AL = 0x00;
        ctx.State.CX = 2; // read 2 sectors
        ctx.State.DX = 0; // starting at sector 0

        ctx.Handler.Run();

        ctx.State.CarryFlag.Should().BeFalse();
        ctx.Memory.UInt8[ctx.BufferAddress + BytesPerSector].Should().Be(0xBB);
    }

    [Fact]
    public void ReadSector_InvalidDrive_ReturnsError() {
        byte[] image = new Fat12ImageBuilder().Build();
        Ctx ctx = new(image);
        ctx.State.AL = 0x01; // drive B — not mounted
        ctx.State.CX = 1;
        ctx.State.DX = 0;

        ctx.Handler.Run();

        ctx.State.CarryFlag.Should().BeTrue();
    }

    [Fact]
    public void ReadSector_HardDisk_ReturnsSuccessWithoutData() {
        byte[] image = new Fat12ImageBuilder().Build();
        Ctx ctx = new(image);
        ctx.State.AL = 0x02; // drive C (hard disk)
        ctx.State.CX = 1;
        ctx.State.DX = 0;
        // buffer should remain unchanged
        ctx.Memory.UInt8[ctx.BufferAddress] = 0xFF;

        ctx.Handler.Run();

        ctx.State.CarryFlag.Should().BeFalse();
        ctx.Memory.UInt8[ctx.BufferAddress].Should().Be(0xFF, "hard disk read is a stub and must not overwrite buffer");
    }

    [Fact]
    public void ReadSector_ExtendedMode_ReadsCorrectSector() {
        byte[] image = new Fat12ImageBuilder().Build();
        image[BytesPerSector * 3] = 0xCC; // sector 3
        Ctx ctx = new(image);

        // Write extended-read structure at DS:BX
        uint structAddr = ctx.BufferAddress;
        ctx.Memory.UInt32[structAddr] = 3;          // starting sector
        ctx.Memory.UInt16[structAddr + 4u] = 1;     // count
        ctx.Memory.UInt16[structAddr + 6u] = 0x0600; // buffer segment
        ctx.Memory.UInt16[structAddr + 8u] = 0x0000; // buffer offset

        ctx.State.AL = 0x00;
        ctx.State.CX = 0xFFFF; // extended mode

        ctx.Handler.Run();

        ctx.State.CarryFlag.Should().BeFalse();
        uint outBuf = MemoryUtils.ToPhysicalAddress(0x0600, 0x0000);
        ctx.Memory.UInt8[outBuf].Should().Be(0xCC);
    }
}
