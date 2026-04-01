namespace Spice86.Tests.Video;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
///     Tests for the <see cref="PlaneAccessor"/> and <see cref="VideoMemory"/> flat interleaved VRam layout.
/// </summary>
public class VRamLayoutTests {
    [Fact]
    public void PlaneAccessor_WriteThenRead_RoundTrips() {
        byte[] vram = new byte[4 * 64 * 1024];
        PlaneAccessor planes = new PlaneAccessor(vram);

        planes[0, 0] = 0xAA;
        planes[1, 0] = 0xBB;
        planes[2, 0] = 0xCC;
        planes[3, 0] = 0xDD;

        planes[0, 0].Should().Be(0xAA);
        planes[1, 0].Should().Be(0xBB);
        planes[2, 0].Should().Be(0xCC);
        planes[3, 0].Should().Be(0xDD);
    }

    [Fact]
    public void PlaneAccessor_MapsToInterleavedLayout() {
        byte[] vram = new byte[4 * 64 * 1024];
        PlaneAccessor planes = new PlaneAccessor(vram);

        planes[2, 5] = 0x42;

        // Interleaved layout: address * 4 + plane
        vram[5 * 4 + 2].Should().Be(0x42);
    }

    [Fact]
    public void PlaneAccessor_DifferentAddresses_AreIndependent() {
        byte[] vram = new byte[4 * 64 * 1024];
        PlaneAccessor planes = new PlaneAccessor(vram);

        planes[0, 100] = 0x11;
        planes[0, 200] = 0x22;

        planes[0, 100].Should().Be(0x11);
        planes[0, 200].Should().Be(0x22);
    }

    [Fact]
    public void VRam_Mode13hChain4_PixelNAtIndexN() {
        // In the interleaved layout, mode 13h chain-4 pixel N maps to VRam[N]:
        // plane = N & 3, offset = N >> 2, index = (N >> 2) * 4 + (N & 3) = N
        byte[] vram = new byte[4 * 64 * 1024];
        PlaneAccessor planes = new PlaneAccessor(vram);

        // Write pixels via plane accessor (as the CPU write path does)
        planes[0, 0] = 10; // pixel 0: plane=0, offset=0 → index 0
        planes[1, 0] = 20; // pixel 1: plane=1, offset=0 → index 1
        planes[2, 0] = 30; // pixel 2: plane=2, offset=0 → index 2
        planes[3, 0] = 40; // pixel 3: plane=3, offset=0 → index 3
        planes[0, 1] = 50; // pixel 4: plane=0, offset=1 → index 4
        planes[1, 1] = 60; // pixel 5: plane=1, offset=1 → index 5

        vram[0].Should().Be(10, "pixel 0 at VRam[0]");
        vram[1].Should().Be(20, "pixel 1 at VRam[1]");
        vram[2].Should().Be(30, "pixel 2 at VRam[2]");
        vram[3].Should().Be(40, "pixel 3 at VRam[3]");
        vram[4].Should().Be(50, "pixel 4 at VRam[4]");
        vram[5].Should().Be(60, "pixel 5 at VRam[5]");
    }

    [Fact]
    public void VideoMemory_VRamAndPlanes_ShareSameStorage() {
        VideoState state = new();
        ILoggerService logger = Substitute.For<ILoggerService>();
        VideoMemory mem = new VideoMemory(state, logger);

        mem.Planes[1, 10] = 0x77;

        mem.VRam[10 * 4 + 1].Should().Be(0x77, "Planes and VRam share the same underlying buffer");
    }

    [Fact]
    public void VideoMemory_GetLinearSpan_ReturnsCorrectSlice() {
        VideoState state = new();
        ILoggerService logger = Substitute.For<ILoggerService>();
        VideoMemory mem = new VideoMemory(state, logger);

        mem.VRam[100] = 0xAA;
        mem.VRam[101] = 0xBB;
        mem.VRam[102] = 0xCC;

        System.ReadOnlySpan<byte> span = mem.GetLinearSpan(100, 3);

        span[0].Should().Be(0xAA);
        span[1].Should().Be(0xBB);
        span[2].Should().Be(0xCC);
    }

    [Fact]
    public void VideoMemory_HasChanged_SetOnWrite() {
        VideoState state = new();
        ILoggerService logger = Substitute.For<ILoggerService>();
        VideoMemory mem = new VideoMemory(state, logger);

        mem.ResetChanged();
        mem.HasChanged.Should().BeFalse();

        // Write a different value via Planes (which writes to VRam)
        mem.Planes[0, 0] = 0;
        mem.ResetChanged();

        // Direct VRam write doesn't go through WriteValue, so HasChanged is the WriteValue path.
        // Use the Write method to test HasChanged properly
        state.SequencerRegisters.PlaneMaskRegister.Value = 0b1111;
        state.SequencerRegisters.MemoryModeRegister.OddEvenMode = false;
        state.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode = WriteMode.WriteMode0;
        state.GraphicsControllerRegisters.EnableSetReset.Value = 0;
        state.GraphicsControllerRegisters.BitMask = 0xFF;
        state.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect = FunctionSelect.None;
        state.GraphicsControllerRegisters.DataRotateRegister.RotateCount = 0;

        uint baseAddr = state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.BaseAddress;
        mem.Write(baseAddr, 0x42);

        mem.HasChanged.Should().BeTrue();
    }

    [Fact]
    public void VideoMemory_LatchLoad_Uses32BitInterleavedRead() {
        VideoState state = new();
        ILoggerService logger = Substitute.For<ILoggerService>();
        VideoMemory mem = new VideoMemory(state, logger);

        // Set up chain4 mode for read
        state.SequencerRegisters.MemoryModeRegister.Chain4Mode = true;
        state.GraphicsControllerRegisters.GraphicsModeRegister.ReadMode = ReadMode.ReadMode0;

        // Write known values to all 4 planes at offset 0
        mem.Planes[0, 0] = 0x11;
        mem.Planes[1, 0] = 0x22;
        mem.Planes[2, 0] = 0x33;
        mem.Planes[3, 0] = 0x44;

        uint baseAddr = state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.BaseAddress;

        // Reading should load all 4 latches from the interleaved buffer
        byte result = mem.Read(baseAddr); // chain4: plane=0, offset=0

        result.Should().Be(0x11, "Read returns plane 0 in ReadMode0 with Chain4");
    }
}
