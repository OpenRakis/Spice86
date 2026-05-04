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

/// <summary>TDD tests for INT 26h Absolute Disk Write.</summary>
public sealed class AbsoluteDiskWriteTests {
    private const ushort BufferSegment = 0x0500;
    private const ushort BufferOffset = 0x0000;
    private const int BytesPerSector = 512;

    private sealed class Ctx {
        public Memory Memory { get; }
        public State State { get; }
        public DosDiskInt26Handler Handler { get; }
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

            DriveManager = DosTestHelpers.CreateDriveManager(logger, null, null);
            DriveManager.MountFloppyImage('A', image, "test.img");

            Handler = new DosDiskInt26Handler(Memory, DriveManager, fhp, stack, State, logger);

            State.DS = BufferSegment;
            State.BX = BufferOffset;
        }

        public uint BufferAddress => MemoryUtils.ToPhysicalAddress(BufferSegment, BufferOffset);
    }

    [Fact]
    public void WriteSector0_FloppyA_PersistsInImage() {
        byte[] image = new Fat12ImageBuilder().Build();
        Ctx ctx = new(image);

        // Fill the write buffer
        ctx.Memory.UInt8[ctx.BufferAddress] = 0xDE;
        ctx.Memory.UInt8[ctx.BufferAddress + 1] = 0xAD;

        ctx.State.AL = 0x00; // drive A
        ctx.State.CX = 1;    // 1 sector
        ctx.State.DX = 0;    // sector 0

        ctx.Handler.Run();

        ctx.State.CarryFlag.Should().BeFalse();
        byte[] readback = new byte[2];
        ctx.DriveManager.TryRead(0, 0, readback, 0, 2);
        readback[0].Should().Be(0xDE);
        readback[1].Should().Be(0xAD);
    }

    [Fact]
    public void WriteSector_InvalidDrive_ReturnsError() {
        byte[] image = new Fat12ImageBuilder().Build();
        Ctx ctx = new(image);
        ctx.State.AL = 0x01; // drive B — not mounted
        ctx.State.CX = 1;
        ctx.State.DX = 0;

        ctx.Handler.Run();

        ctx.State.CarryFlag.Should().BeTrue();
    }

    [Fact]
    public void WriteSector_HardDisk_ReturnsSuccess() {
        byte[] image = new Fat12ImageBuilder().Build();
        Ctx ctx = new(image);
        ctx.State.AL = 0x02; // drive C (hard disk stub)
        ctx.State.CX = 1;
        ctx.State.DX = 0;

        ctx.Handler.Run();

        ctx.State.CarryFlag.Should().BeFalse();
    }

    [Fact]
    public void WriteSector_ExtendedMode_WritesCorrectSector() {
        byte[] image = new Fat12ImageBuilder().Build();
        Ctx ctx = new(image);

        // Set up source buffer at a different segment
        ushort srcSeg = 0x0600;
        uint srcAddr = MemoryUtils.ToPhysicalAddress(srcSeg, 0);
        ctx.Memory.UInt8[srcAddr] = 0xEE;

        // Write extended-write structure at DS:BX
        uint structAddr = ctx.BufferAddress;
        ctx.Memory.UInt32[structAddr] = 5;           // target sector 5
        ctx.Memory.UInt16[structAddr + 4u] = 1;      // count
        ctx.Memory.UInt16[structAddr + 6u] = srcSeg; // buffer segment
        ctx.Memory.UInt16[structAddr + 8u] = 0x0000; // buffer offset

        ctx.State.AL = 0x00;
        ctx.State.CX = 0xFFFF;

        ctx.Handler.Run();

        ctx.State.CarryFlag.Should().BeFalse();
        byte[] readback = new byte[1];
        ctx.DriveManager.TryRead(0, 5 * BytesPerSector, readback, 0, 1);
        readback[0].Should().Be(0xEE, "written byte must appear at sector 5");
    }
}
