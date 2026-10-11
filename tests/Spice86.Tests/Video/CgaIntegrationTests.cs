namespace Spice86.Tests.Video;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers.Enums;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

using Xunit;

public sealed class CgaIntegrationTests {
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void PackedPixels_UseGuestMemoryOrderingBanksAndStride(int mode) {
        // Arrange
        VideoHarness video = new(mode);
        video.Memory.UInt8[0xB8000] = 0x1B;
        video.Memory.UInt8[0xB8001] = 0xE4;
        video.Memory.UInt8[0xBA000] = 0x6C;
        video.Memory.UInt8[0xB8050] = 0xFF;
        video.Memory.UInt8[0xBBF3F] = 0xFF;
        uint[] colors = video.State.DacRegisters.AttributeMap;

        // Act
        uint[] frame = video.Render();

        // Assert
        frame.Take(8).Should().Equal(colors[0], colors[1], colors[2], colors[3],
            colors[3], colors[2], colors[1], colors[0]);
        frame.Skip(320).Take(4).Should().Equal(colors[1], colors[2], colors[3], colors[0]);
        frame.Skip(640).Take(4).Should().AllBeEquivalentTo(colors[3]);
        frame.Skip(199 * 320 + 316).Take(4).Should().AllBeEquivalentTo(colors[3]);
        frame.Length.Should().Be(320 * 200);
    }

    [Theory]
    [InlineData(0x00, 0xFF000000U, 0xFF00AA00U, 0xFFAA0000U, 0xFFAA5500U)]
    [InlineData(0x20, 0xFF000000U, 0xFF00AAAAU, 0xFFAA00AAU, 0xFFAAAAAAU)]
    [InlineData(0x30, 0xFF000000U, 0xFF55FFFFU, 0xFFFF55FFU, 0xFFFFFFFFU)]
    [InlineData(0x1E, 0xFFFFFF55U, 0xFF55FF55U, 0xFFFF5555U, 0xFFFFFF55U)]
    public void ColorPort_UpdatesRenderedBackgroundPaletteAndIntensity(
        byte colorSelect, uint background, uint color1, uint color2, uint color3) {
        // Arrange
        VideoHarness video = new(4);
        video.Memory.UInt8[0xB8000] = 0x1B;

        // Act
        video.Ports.WriteByte(Ports.CgaColorSelect, colorSelect);
        uint[] frame = video.Render();

        // Assert
        frame.Take(4).Should().Equal(background, color1, color2, color3);
        video.BiosDataArea.VideoPal.Should().Be(0x30, "raw hardware writes do not update BIOS mirrors");
    }

    [Fact]
    public void ColorPort_RepaintsAnUnchangedVideoBuffer() {
        // Arrange
        VideoHarness video = new(4);
        video.Memory.UInt8[0xB8000] = 0x55;
        video.Render()[0].Should().Be(0xFF55FFFF);
        video.State.IsRenderingDirty.Should().BeFalse();

        // Act
        video.Ports.WriteByte(Ports.CgaColorSelect, 0x20);
        uint[] frame = video.Render();

        // Assert
        frame.Take(4).Should().AllBeEquivalentTo(0xFF00AAAAU);
        video.Memory.UInt8[0xB8000].Should().Be(0x55);
    }

    [Fact]
    public void ModeControl_SelectsCyanRedWhiteWithoutChangingBiosMirror() {
        // Arrange
        VideoHarness video = new(4);
        video.Memory.UInt8[0xB8000] = 0x1B;
        video.Ports.WriteByte(Ports.CgaColorSelect, 0);

        // Act
        video.Ports.WriteByte(Ports.CgaModeControl, 0x2E);
        uint[] frame = video.Render();

        // Assert
        frame.Take(4).Should().Equal(0xFF000000U, 0xFF00AAAAU, 0xFFAA0000U, 0xFFAAAAAAU);
        video.BiosDataArea.VideoMsr.Should().Be(0x2A);
    }

    [Fact]
    public void ModeControl_DisablesAndRestoresVideoWithoutChangingMemory() {
        // Arrange
        VideoHarness video = new(4);
        video.Memory.UInt8[0xB8000] = 0xFF;
        uint foreground = video.Render()[0];

        // Act
        video.Ports.WriteByte(Ports.CgaModeControl, 0x22);
        uint[] blank = video.Render();
        video.Ports.WriteByte(Ports.CgaModeControl, 0x2A);
        uint[] restored = video.Render();

        // Assert
        blank.Should().AllBeEquivalentTo(0xFF000000U);
        restored.Take(4).Should().AllBeEquivalentTo(foreground);
        video.Memory.UInt8[0xB8000].Should().Be(0xFF);
    }

    [Fact]
    public void Mode5_UsesTheBlackAndWhiteModePalette() {
        // Arrange
        VideoHarness video = new(5);
        video.Memory.UInt8[0xB8000] = 0x1B;

        // Act
        video.Ports.WriteByte(Ports.CgaColorSelect, 0);
        uint[] frame = video.Render();

        // Assert
        frame.Take(4).Should().Equal(0xFF000000U, 0xFF00AAAAU, 0xFFAA0000U, 0xFFAAAAAAU);
        video.BiosDataArea.VideoMsr.Should().Be(0x2E);
    }

    [Fact]
    public void Mode6_UsesPackedBitsAndTheSelectedForeground() {
        // Arrange
        VideoHarness video = new(6);
        video.Memory.UInt8[0xB8000] = 0xA5;
        video.Memory.UInt8[0xBA000] = 0x80;
        video.Memory.UInt8[0xBBF3F] = 0x01;

        // Act
        video.Ports.WriteByte(Ports.CgaColorSelect, 0x14);
        uint[] frame = video.Render();

        // Assert
        frame.Take(8).Should().Equal(0xFFAA0000U, 0xFF000000U, 0xFFAA0000U, 0xFF000000U,
            0xFF000000U, 0xFFAA0000U, 0xFF000000U, 0xFFAA0000U);
        frame[640].Should().Be(0xFFAA0000U);
        frame[^1].Should().Be(0xFFAA0000U);
        frame.Length.Should().Be(640 * 200);
    }

    [Fact]
    public void StartAddress_OffsetsPackedGuestMemory() {
        // Arrange
        VideoHarness video = new(4);
        video.Memory.UInt8[0xB8002] = 0xFF;

        // Act
        video.Ports.WriteByte(Ports.CrtControllerAddress, (byte)CrtControllerRegister.StartAddressLow);
        video.Ports.WriteByte(Ports.CrtControllerData, 1);
        uint[] frame = video.Render();

        // Assert
        frame.Take(4).Should().AllBeEquivalentTo(video.State.DacRegisters.AttributeMap[3]);
    }

    [Fact]
    public void BiosPaletteCalls_PreserveUnrelatedMirrorBitsAndUpdateHardware() {
        // Arrange
        VideoHarness video = new(4);
        video.BiosDataArea.VideoPal = 0xB5;
        video.Memory.UInt8[0xB8000] = 0x1B;

        // Act
        video.Bios.SetBorderColor(0x1E);
        byte afterBackground = video.BiosDataArea.VideoPal;
        video.Bios.SetPalette(0);
        uint[] frame = video.Render();

        // Assert
        afterBackground.Should().Be(0xBE);
        video.BiosDataArea.VideoPal.Should().Be(0x9E);
        frame.Take(4).Should().Equal(0xFFFFFF55U, 0xFF55FF55U, 0xFFFF5555U, 0xFFFFFF55U);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0x0D)]
    [InlineData(0x13)]
    public void CgaPortWrites_DoNotChangeNonCgaPalettes(int mode) {
        // Arrange
        VideoHarness video = new(mode);
        uint[] colors = video.State.DacRegisters.AttributeMap.ToArray();
        byte[] attributes = video.State.AttributeControllerRegisters.InternalPalette.ToArray();
        if (mode == 3) {
            video.Memory.UInt16[0xB8000] = 0x1F41;
        } else {
            video.Memory.UInt8[0xA0000] = 0x80;
        }
        uint[] originalFrame = video.Render();

        // Act
        video.Ports.WriteByte(Ports.CgaColorSelect, 0x1E);
        video.Ports.WriteByte(Ports.CgaModeControl, 0x2E);

        // Assert
        video.State.DacRegisters.AttributeMap.Should().Equal(colors);
        video.State.AttributeControllerRegisters.InternalPalette.Should().Equal(attributes);
        video.Render().Should().Equal(originalFrame);
    }

    [Theory]
    [InlineData(Ports.CgaModeControl)]
    [InlineData(Ports.CgaColorSelect)]
    public void CgaRegisters_AreWriteOnly(ushort port) {
        // Arrange
        VideoHarness video = new(4);
        video.Ports.WriteByte(port, 0x20);

        // Act
        byte value = video.Ports.ReadByte(port);

        // Assert
        value.Should().Be(0xFF, "the write-only CGA latches do not expose stored values on the data bus");
    }

    [Fact]
    public void ModeChanges_RestoreCgaDefaultsAfterOtherGraphicsModes() {
        // Arrange
        VideoHarness video = new(4);
        video.Ports.WriteByte(Ports.CgaColorSelect, 0x1E);
        video.Bios.VgaSetMode(0x13, ModeFlags.Legacy);

        // Act
        video.Bios.VgaSetMode(4, ModeFlags.Legacy);
        video.Memory.UInt8[0xB8000] = 0x1B;
        uint[] frame = video.Render();

        // Assert
        frame.Take(4).Should().Equal(0xFF000000U, 0xFF55FFFFU, 0xFFFF55FFU, 0xFFFFFFFFU);
        video.BiosDataArea.VideoPal.Should().Be(0x30);
        video.BiosDataArea.VideoMsr.Should().Be(0x2A);
    }

    private sealed class VideoHarness {
        public VideoHarness(int mode) {
            Memory = new Memory(new(), new Ram(0x100000), new A20Gate(), new RealModeMmu386(), false);
            State cpuState = new(CpuModel.ZET_86);
            Ports = new IOPortDispatcher(new(), cpuState, NullLogger.Instance, true);
            State = new VideoState();
            _ = new VgaIoPortHandler(cpuState, Ports, NullLogger.Instance, State, true);
            Renderer = new Renderer(Memory, State, new VgaBlinkState(), NullLogger.Instance, new VgaRenderer256ColorScalar());
            VgaRom rom = new(NullLogger.Instance);
            Memory.RegisterMapping(VgaRom.Segment << 4, rom.Size, rom);
            BiosDataArea = new BiosDataArea(Memory, 640);
            Bios = new VgaFunctionality(Memory, new InterruptVectorTable(Memory), Ports, BiosDataArea, rom, State, false);
            Bios.VgaSetMode(mode, ModeFlags.Legacy);
        }

        public IMemory Memory { get; }
        public IOPortDispatcher Ports { get; }
        public VideoState State { get; }
        public Renderer Renderer { get; }
        public BiosDataArea BiosDataArea { get; }
        public VgaFunctionality Bios { get; }

        public uint[] Render() {
            Renderer.BeginFrame();
            for (int i = 0; i < State.CrtControllerRegisters.VerticalTotalValue + 2; i++) {
                Renderer.RenderScanline();
            }
            Renderer.CompleteFrame();
            uint[] frame = new uint[Renderer.Width * Renderer.Height];
            Renderer.Render(frame);
            return frame;
        }
    }
}
