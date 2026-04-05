namespace Spice86.Tests.Video;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Video;

using Xunit;

/// <summary>
///     Unit tests for the MemoryBitmapRenderer static rendering methods.
/// </summary>
public class MemoryBitmapRendererTests {
    [Fact]
    public void Render_Raw8Bpp_ProducesCorrectPixelCount() {
        byte[] data = new byte[320 * 200];
        for (int i = 0; i < data.Length; i++) {
            data[i] = (byte)(i % 256);
        }

        uint[] pixels = MemoryBitmapRenderer.Render(data, 320, 200, MemoryBitmapVideoMode.Raw8Bpp, null);

        pixels.Length.Should().Be(320 * 200);
    }

    [Fact]
    public void Render_Raw8Bpp_MapsColorsThroughPalette() {
        uint[] palette = [0xFF000000, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF];
        byte[] data = [0, 1, 2, 3];

        uint[] pixels = MemoryBitmapRenderer.Render(data, 4, 1, MemoryBitmapVideoMode.Raw8Bpp, palette);

        pixels.Should().Equal(0xFF000000U, 0xFFFF0000U, 0xFF00FF00U, 0xFF0000FFU);
    }

    [Fact]
    public void Render_Vga256_IdenticalToRaw8Bpp() {
        byte[] data = new byte[10];
        for (int i = 0; i < data.Length; i++) {
            data[i] = (byte)i;
        }
        uint[] palette = MemoryBitmapRenderer.DefaultVga256Palette;

        uint[] rawPixels = MemoryBitmapRenderer.Render(data, 10, 1, MemoryBitmapVideoMode.Raw8Bpp, palette);
        uint[] vgaPixels = MemoryBitmapRenderer.Render(data, 10, 1, MemoryBitmapVideoMode.Vga256Color, palette);

        rawPixels.Should().Equal(vgaPixels);
    }

    [Fact]
    public void Render_Ega16_ProducesCorrectPixelCount() {
        // 8 pixels wide, 1 row, 4 planes
        int bytesPerRow = 1;
        byte[] data = new byte[bytesPerRow * 1 * 4];
        data[0] = 0xFF;  // Plane 0: all bits set
        data[1] = 0x00;  // Plane 1: all bits clear
        data[2] = 0x00;  // Plane 2: all bits clear
        data[3] = 0x00;  // Plane 3: all bits clear

        uint[] pixels = MemoryBitmapRenderer.Render(data, 8, 1, MemoryBitmapVideoMode.Ega16Color, null);

        pixels.Length.Should().Be(8);
        // All pixels should have color index 1 (plane 0 set, others clear)
        uint[] egaPalette = MemoryBitmapRenderer.DefaultEga16Palette;
        for (int i = 0; i < 8; i++) {
            pixels[i].Should().Be(egaPalette[1]);
        }
    }

    [Fact]
    public void Render_Ega16_PlanesInterleaveCorrectly() {
        byte[] data = new byte[4]; // 1 byte per plane, 4 planes
        data[0] = 0xF0;  // Plane 0: upper nibble set
        data[1] = 0x0F;  // Plane 1: lower nibble set
        data[2] = 0x00;  // Plane 2: clear
        data[3] = 0x00;  // Plane 3: clear

        uint[] palette = MemoryBitmapRenderer.DefaultEga16Palette;
        uint[] pixels = MemoryBitmapRenderer.Render(data, 8, 1, MemoryBitmapVideoMode.Ega16Color, palette);

        // Pixels 0-3: plane0 bit set -> index 1
        // Pixels 4-7: plane1 bit set -> index 2
        pixels[0].Should().Be(palette[1]);
        pixels[4].Should().Be(palette[2]);
    }

    [Fact]
    public void Render_Cga4Color_ProducesCorrectPixelCount() {
        byte[] data = new byte[80 * 100]; // 320x200 at 2bpp, interleaved banks
        uint[] pixels = MemoryBitmapRenderer.Render(data, 320, 200, MemoryBitmapVideoMode.Cga4Color, null);

        pixels.Length.Should().Be(320 * 200);
    }

    [Fact]
    public void Render_Cga2Color_ProducesCorrectPixelCount() {
        byte[] data = new byte[80 * 100]; // 640x200 at 1bpp, interleaved banks
        uint[] pixels = MemoryBitmapRenderer.Render(data, 640, 200, MemoryBitmapVideoMode.Cga2Color, null);

        pixels.Length.Should().Be(640 * 200);
    }

    [Fact]
    public void Render_Text_ProducesCorrectPixelDimensions() {
        byte[] data = new byte[80 * 25 * 2]; // 80x25 text mode
        data[0] = (byte)'A';
        data[1] = 0x07; // Light grey on black

        uint[] pixels = MemoryBitmapRenderer.Render(data, 80, 25, MemoryBitmapVideoMode.Text, null);

        // 80 chars × 8px = 640, 25 rows × 16px = 400
        pixels.Length.Should().Be(640 * 400);
    }

    [Fact]
    public void Render_Text_ForegroundAndBackgroundColors() {
        uint[] palette = MemoryBitmapRenderer.DefaultCga16Palette;
        byte[] data = new byte[2];
        data[0] = 0x20; // Space character (all background)
        data[1] = 0x1F; // White on blue (fg=0xF, bg=0x1)

        uint[] pixels = MemoryBitmapRenderer.Render(data, 1, 1, MemoryBitmapVideoMode.Text, palette);

        // Space renders as all background pixels
        // 8x16 = 128 pixels, all should be background color (palette[1])
        pixels.Length.Should().Be(128);
        pixels[0].Should().Be(palette[1]); // Background color (blue)
    }

    [Fact]
    public void Render_EmptyData_ReturnsEmptyArray() {
        uint[] pixels = MemoryBitmapRenderer.Render([], 320, 200, MemoryBitmapVideoMode.Vga256Color, null);
        pixels.Should().BeEmpty();
    }

    [Fact]
    public void Render_ZeroDimensions_ReturnsEmptyArray() {
        byte[] data = [0, 1, 2, 3];
        uint[] pixels = MemoryBitmapRenderer.Render(data, 0, 200, MemoryBitmapVideoMode.Vga256Color, null);
        pixels.Should().BeEmpty();
    }

    [Fact]
    public void Render_DataShorterThanBitmap_PadsWithBlack() {
        byte[] data = [1]; // Only 1 byte for a 4x1 bitmap
        uint[] palette = MemoryBitmapRenderer.DefaultVga256Palette;

        uint[] pixels = MemoryBitmapRenderer.Render(data, 4, 1, MemoryBitmapVideoMode.Raw8Bpp, palette);

        pixels.Length.Should().Be(4);
        pixels[0].Should().Be(palette[1]); // Color index 1
        pixels[1].Should().Be(0xFF000000U); // Black (padding)
        pixels[2].Should().Be(0xFF000000U);
        pixels[3].Should().Be(0xFF000000U);
    }

    [Fact]
    public void BuildArgbPalette_ConvertsCorrectly() {
        byte[] rgb6 = [0x3F, 0x00, 0x00]; // Bright red (6-bit max)
        uint[] argb = MemoryBitmapRenderer.BuildArgbPalette(rgb6, 1);

        argb.Length.Should().Be(1);
        // 0x3F expanded: (0x3F << 2) | (0x3F >> 4) = 0xFC | 0x03 = 0xFF
        argb[0].Should().Be(0xFFFF0000U); // Pure red
    }

    [Fact]
    public void GetOutputPixelWidth_TextMode_MultipliedBy8() {
        int result = MemoryBitmapRenderer.GetOutputPixelWidth(MemoryBitmapVideoMode.Text, 80);
        result.Should().Be(640);
    }

    [Fact]
    public void GetOutputPixelWidth_GraphicsMode_Unchanged() {
        int result = MemoryBitmapRenderer.GetOutputPixelWidth(MemoryBitmapVideoMode.Vga256Color, 320);
        result.Should().Be(320);
    }

    [Fact]
    public void GetOutputPixelHeight_TextMode_MultipliedBy16() {
        int result = MemoryBitmapRenderer.GetOutputPixelHeight(MemoryBitmapVideoMode.Text, 25);
        result.Should().Be(400);
    }

    [Fact]
    public void DefaultPalettes_HaveCorrectSize() {
        MemoryBitmapRenderer.DefaultVga256Palette.Length.Should().Be(256);
        MemoryBitmapRenderer.DefaultCga16Palette.Length.Should().Be(16);
        MemoryBitmapRenderer.DefaultEga16Palette.Length.Should().Be(16);
        MemoryBitmapRenderer.MonochromePalette.Length.Should().Be(2);
    }

    [Fact]
    public void DefaultPalettes_AllHaveFullAlpha() {
        foreach (uint color in MemoryBitmapRenderer.DefaultVga256Palette) {
            (color >> 24).Should().Be(0xFF, "all palette colors should have full alpha");
        }
    }

    [Fact]
    public void Render_Cga4Color_ExtractsTwoBitsPerPixel() {
        // Single byte: 0b11_10_01_00 = 0xE4
        // Should produce 4 pixels with indices 3, 2, 1, 0
        uint[] palette = [0xFF000000, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF];
        byte[] data = new byte[2]; // Minimum for CGA with interleaved banks
        data[0] = 0xE4; // Even bank, row 0

        uint[] pixels = MemoryBitmapRenderer.Render(data, 4, 1, MemoryBitmapVideoMode.Cga4Color, palette);

        pixels.Length.Should().Be(4);
        pixels[0].Should().Be(palette[3]); // 0b11
        pixels[1].Should().Be(palette[2]); // 0b10
        pixels[2].Should().Be(palette[1]); // 0b01
        pixels[3].Should().Be(palette[0]); // 0b00
    }

    [Fact]
    public void Render_Cga2Color_ExtractsOneBitPerPixel() {
        uint[] palette = [0xFF000000, 0xFFFFFFFF];
        byte[] data = new byte[2]; // Minimum for CGA with interleaved banks
        data[0] = 0xAA; // Even bank: 10101010 = alternating pixels

        uint[] pixels = MemoryBitmapRenderer.Render(data, 8, 1, MemoryBitmapVideoMode.Cga2Color, palette);

        pixels.Length.Should().Be(8);
        pixels[0].Should().Be(palette[1]); // bit 7 set
        pixels[1].Should().Be(palette[0]); // bit 6 clear
        pixels[2].Should().Be(palette[1]); // bit 5 set
        pixels[3].Should().Be(palette[0]); // bit 4 clear
    }
}
