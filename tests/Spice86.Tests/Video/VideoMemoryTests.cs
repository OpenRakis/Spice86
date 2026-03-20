namespace Spice86.Tests.Video;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;
using Spice86.Shared.Interfaces;

using Xunit;

public class VideoMemoryTests {
    [Fact]
    public void WriteMode2_UsesLowNibbleBitsForPlaneData() {
        // Arrange
        VideoState state = new();
        state.SequencerRegisters.MemoryModeRegister.OddEvenMode = false;
        state.SequencerRegisters.PlaneMaskRegister.Value = 0b1111;
        state.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode = WriteMode.WriteMode2;
        state.GraphicsControllerRegisters.BitMask = 0xFF;

        VideoMemory sut = CreateVideoMemory(state);

        uint address = state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.BaseAddress;

        // Act
        sut.Write(address, 0b0000_0101);

        // Assert
        Assert.Equal(0xFF, sut.Planes[0, 0]);
        Assert.Equal(0x00, sut.Planes[1, 0]);
        Assert.Equal(0xFF, sut.Planes[2, 0]);
        Assert.Equal(0x00, sut.Planes[3, 0]);
    }

    [Fact]
    public void WriteMode1_InChain4_WritesOnlyDecodedPlane() {
        // Arrange
        VideoState state = new();
        state.SequencerRegisters.PlaneMaskRegister.Value = 0b1111;
        state.SequencerRegisters.MemoryModeRegister.Chain4Mode = true;
        state.GraphicsControllerRegisters.GraphicsModeRegister.ReadMode = ReadMode.ReadMode0;
        state.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode = WriteMode.WriteMode1;

        VideoMemory sut = CreateVideoMemory(state);
        uint baseAddress = state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.BaseAddress;

        sut.Planes[0, 0] = 0x10;
        sut.Planes[1, 0] = 0x20;
        sut.Planes[2, 0] = 0x30;
        sut.Planes[3, 0] = 0x40;
        _ = sut.Read(baseAddress);

        sut.Planes[0, 0] = 0x01;
        sut.Planes[1, 0] = 0x02;
        sut.Planes[2, 0] = 0x03;
        sut.Planes[3, 0] = 0x04;

        // Act
        sut.Write(baseAddress + 1, 0x00);

        // Assert
        Assert.Equal(0x01, sut.Planes[0, 0]);
        Assert.Equal(0x20, sut.Planes[1, 0]);
        Assert.Equal(0x03, sut.Planes[2, 0]);
        Assert.Equal(0x04, sut.Planes[3, 0]);
    }

    private static VideoMemory CreateVideoMemory(VideoState state) {
        ILoggerService logger = Substitute.For<ILoggerService>();
        return new VideoMemory(state, logger);
    }
}
