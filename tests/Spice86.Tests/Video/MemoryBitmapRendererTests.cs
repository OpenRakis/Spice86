namespace Spice86.Tests.Video;

using FluentAssertions;

using Spice86.ViewModels.Services.Rendering;

using Xunit;

/// <summary>
///     Unit tests for the MemoryBitmapRenderer static rendering methods.
/// </summary>
public class MemoryBitmapRendererTests {
    [Fact]
    public void Render_Raw8Bpp_ProducesCorrectPixelCount() {
        // Arrange
        byte[] data = new byte[320 * 200];
        for (int i = 0; i < data.Length; i++) {
            data[i] = (byte)(i % 256);
        }

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 320, 200, MemoryBitmapVideoMode.Raw8Bpp, null);

        // Assert
        pixels.Length.Should().Be(320 * 200);
    }

    [Fact]
    public void Render_Raw8Bpp_MapsColorsThroughPalette() {
        // Arrange
        uint[] palette = [0xFF000000, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF];
        byte[] data = [0, 1, 2, 3];

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 4, 1, MemoryBitmapVideoMode.Raw8Bpp, palette);

        // Assert
        pixels.Should().Equal(0xFF000000U, 0xFFFF0000U, 0xFF00FF00U, 0xFF0000FFU);
    }

    [Fact]
    public void Render_Vga256_IdenticalToRaw8Bpp() {
        // Arrange
        byte[] data = new byte[10];
        for (int i = 0; i < data.Length; i++) {
            data[i] = (byte)i;
        }
        uint[] palette = MemoryBitmapRenderer.DefaultVga256Palette;

        // Act
        uint[] rawPixels = MemoryBitmapRenderer.Render(data, 10, 1, MemoryBitmapVideoMode.Raw8Bpp, palette);
        uint[] vgaPixels = MemoryBitmapRenderer.Render(data, 10, 1, MemoryBitmapVideoMode.Vga256Color, palette);

        // Assert
        rawPixels.Should().Equal(vgaPixels);
    }

    [Fact]
    public void Render_Ega16_ProducesCorrectPixelCount() {
        // Arrange
        int bytesPerRow = 1;
        byte[] data = new byte[bytesPerRow * 1 * 4];
        data[0] = 0xFF;  // Plane 0: all bits set
        data[1] = 0x00;  // Plane 1: all bits clear
        data[2] = 0x00;  // Plane 2: all bits clear
        data[3] = 0x00;  // Plane 3: all bits clear

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 8, 1, MemoryBitmapVideoMode.Ega16Color, null);

        // Assert
        pixels.Length.Should().Be(8);
        uint[] egaPalette = MemoryBitmapRenderer.DefaultEga16Palette;
        for (int i = 0; i < 8; i++) {
            pixels[i].Should().Be(egaPalette[1]);
        }
    }

    [Fact]
    public void Render_Ega16_PlanesInterleaveCorrectly() {
        // Arrange
        byte[] data = new byte[4];
        data[0] = 0xF0;  // Plane 0: upper nibble set
        data[1] = 0x0F;  // Plane 1: lower nibble set
        data[2] = 0x00;  // Plane 2: clear
        data[3] = 0x00;  // Plane 3: clear
        uint[] palette = MemoryBitmapRenderer.DefaultEga16Palette;

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 8, 1, MemoryBitmapVideoMode.Ega16Color, palette);

        // Assert
        pixels[0].Should().Be(palette[1]);
        pixels[4].Should().Be(palette[2]);
    }

    [Fact]
    public void Render_Cga4Color_ProducesCorrectPixelCount() {
        // Arrange
        byte[] data = new byte[80 * 100];

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 320, 200, MemoryBitmapVideoMode.Cga4Color, null);

        // Assert
        pixels.Length.Should().Be(320 * 200);
    }

    [Fact]
    public void Render_Cga2Color_ProducesCorrectPixelCount() {
        // Arrange
        byte[] data = new byte[80 * 100];

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 640, 200, MemoryBitmapVideoMode.Cga2Color, null);

        // Assert
        pixels.Length.Should().Be(640 * 200);
    }

    [Fact]
    public void Render_Text_ProducesCorrectPixelDimensions() {
        // Arrange
        byte[] data = new byte[80 * 25 * 2];
        data[0] = (byte)'A';
        data[1] = 0x07;

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 80, 25, MemoryBitmapVideoMode.Text, null);

        // Assert
        pixels.Length.Should().Be(640 * 400);
    }

    [Fact]
    public void Render_Text_ForegroundAndBackgroundColors() {
        // Arrange
        uint[] palette = MemoryBitmapRenderer.DefaultCga16Palette;
        byte[] data = new byte[2];
        data[0] = 0x20; // Space character (all background)
        data[1] = 0x1F; // White on blue (fg=0xF, bg=0x1)

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 1, 1, MemoryBitmapVideoMode.Text, palette);

        // Assert
        pixels.Length.Should().Be(128);
        pixels[0].Should().Be(palette[1]);
    }

    [Fact]
    public void Render_EmptyData_ReturnsEmptyArray() {
        // Arrange
        ReadOnlySpan<byte> emptyData = [];

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(emptyData, 320, 200, MemoryBitmapVideoMode.Vga256Color, null);

        // Assert
        pixels.Should().BeEmpty();
    }

    [Fact]
    public void Render_ZeroDimensions_ReturnsEmptyArray() {
        // Arrange
        byte[] data = [0, 1, 2, 3];

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 0, 200, MemoryBitmapVideoMode.Vga256Color, null);

        // Assert
        pixels.Should().BeEmpty();
    }

    [Fact]
    public void Render_DataShorterThanBitmap_PadsWithBlack() {
        // Arrange
        byte[] data = [1];
        uint[] palette = MemoryBitmapRenderer.DefaultVga256Palette;

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 4, 1, MemoryBitmapVideoMode.Raw8Bpp, palette);

        // Assert
        pixels.Length.Should().Be(4);
        pixels[0].Should().Be(palette[1]);
        pixels[1].Should().Be(0xFF000000U);
        pixels[2].Should().Be(0xFF000000U);
        pixels[3].Should().Be(0xFF000000U);
    }

    [Fact]
    public void BuildArgbPalette_ConvertsCorrectly() {
        // Arrange
        byte[] rgb6 = [0x3F, 0x00, 0x00];

        // Act
        uint[] argb = MemoryBitmapRenderer.BuildArgbPalette(rgb6, 1);

        // Assert
        argb.Length.Should().Be(1);
        argb[0].Should().Be(0xFFFF0000U);
    }

    [Fact]
    public void GetOutputPixelWidth_TextMode_MultipliedBy8() {
        // Arrange & Act
        int result = MemoryBitmapRenderer.GetOutputPixelWidth(MemoryBitmapVideoMode.Text, 80);

        // Assert
        result.Should().Be(640);
    }

    [Fact]
    public void GetOutputPixelWidth_GraphicsMode_Unchanged() {
        // Arrange & Act
        int result = MemoryBitmapRenderer.GetOutputPixelWidth(MemoryBitmapVideoMode.Vga256Color, 320);

        // Assert
        result.Should().Be(320);
    }

    [Fact]
    public void GetOutputPixelHeight_TextMode_MultipliedBy16() {
        // Arrange & Act
        int result = MemoryBitmapRenderer.GetOutputPixelHeight(MemoryBitmapVideoMode.Text, 25);

        // Assert
        result.Should().Be(400);
    }

    [Fact]
    public void DefaultPalettes_HaveCorrectSize() {
        // Arrange & Act & Assert
        MemoryBitmapRenderer.DefaultVga256Palette.Length.Should().Be(256);
        MemoryBitmapRenderer.DefaultCga16Palette.Length.Should().Be(16);
        MemoryBitmapRenderer.DefaultEga16Palette.Length.Should().Be(16);
        MemoryBitmapRenderer.MonochromePalette.Length.Should().Be(2);
    }

    [Fact]
    public void DefaultPalettes_AllHaveFullAlpha() {
        // Arrange
        uint[] palette = MemoryBitmapRenderer.DefaultVga256Palette;

        // Act & Assert
        foreach (uint color in palette) {
            (color >> 24).Should().Be(0xFF, "all palette colors should have full alpha");
        }
    }

    [Fact]
    public void Render_Cga4Color_ExtractsTwoBitsPerPixel() {
        // Arrange
        uint[] palette = [0xFF000000, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF];
        byte[] data = new byte[2];
        data[0] = 0xE4; // 0b11_10_01_00

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 4, 1, MemoryBitmapVideoMode.Cga4Color, palette);

        // Assert
        pixels.Length.Should().Be(4);
        pixels[0].Should().Be(palette[3]);
        pixels[1].Should().Be(palette[2]);
        pixels[2].Should().Be(palette[1]);
        pixels[3].Should().Be(palette[0]);
    }

    [Fact]
    public void Render_Cga2Color_ExtractsOneBitPerPixel() {
        // Arrange
        uint[] palette = [0xFF000000, 0xFFFFFFFF];
        byte[] data = new byte[2];
        data[0] = 0xAA; // 10101010

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 8, 1, MemoryBitmapVideoMode.Cga2Color, palette);

        // Assert
        pixels.Length.Should().Be(8);
        pixels[0].Should().Be(palette[1]);
        pixels[1].Should().Be(palette[0]);
        pixels[2].Should().Be(palette[1]);
        pixels[3].Should().Be(palette[0]);
    }

    [Fact]
    public void Render_VgaModeX_ProducesCorrectPixelCount() {
        // Arrange
        byte[] data = new byte[4];
        data[0] = 1;
        data[1] = 2;
        data[2] = 3;
        data[3] = 4;
        uint[] palette = MemoryBitmapRenderer.DefaultVga256Palette;

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 4, 1, MemoryBitmapVideoMode.VgaModeX, palette);

        // Assert
        pixels.Length.Should().Be(4);
        pixels[0].Should().Be(palette[1]);
        pixels[1].Should().Be(palette[2]);
        pixels[2].Should().Be(palette[3]);
        pixels[3].Should().Be(palette[4]);
    }

    [Fact]
    public void Render_VgaModeX_ProducesCorrectDimensionForTypicalMode() {
        // Arrange
        byte[] data = new byte[320 * 240];

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 320, 240, MemoryBitmapVideoMode.VgaModeX, null);

        // Assert
        pixels.Length.Should().Be(320 * 240);
    }

    [Fact]
    public void Render_Packed4Bpp_TwoPixelsPerByte() {
        // Arrange
        uint[] palette = [0xFF000000, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF,
            0xFFFFFF00, 0xFFFF00FF, 0xFF00FFFF, 0xFFFFFFFF,
            0xFF808080, 0xFF800000, 0xFF008000, 0xFF000080,
            0xFF808000, 0xFF800080, 0xFF008080, 0xFFC0C0C0];
        byte[] data = [0xA5]; // high nibble 0xA, low nibble 0x5

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 2, 1, MemoryBitmapVideoMode.Packed4Bpp, palette);

        // Assert
        pixels.Length.Should().Be(2);
        pixels[0].Should().Be(palette[0x0A]);
        pixels[1].Should().Be(palette[0x05]);
    }

    [Fact]
    public void Render_Packed4Bpp_ProducesCorrectPixelCount() {
        // Arrange
        byte[] data = new byte[160 * 200];

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 320, 200, MemoryBitmapVideoMode.Packed4Bpp, null);

        // Assert
        pixels.Length.Should().Be(320 * 200);
    }

    [Fact]
    public void Render_Linear1Bpp_NoInterleaving() {
        // Arrange
        uint[] palette = [0xFF000000, 0xFFFFFFFF];
        byte[] data = [0xFF, 0x00]; // Row 0: all white, Row 1: all black

        // Act
        uint[] pixels = MemoryBitmapRenderer.Render(data, 8, 2, MemoryBitmapVideoMode.Linear1Bpp, palette);

        // Assert
        pixels.Length.Should().Be(16);
        for (int i = 0; i < 8; i++) {
            pixels[i].Should().Be(palette[1], $"row 0, pixel {i} should be white");
        }
        for (int i = 8; i < 16; i++) {
            pixels[i].Should().Be(palette[0], $"row 1, pixel {i - 8} should be black");
        }
    }

    [Fact]
    public void Render_Linear1Bpp_DiffersFromCga2Color() {
        // Arrange
        uint[] palette = [0xFF000000, 0xFFFFFFFF];
        byte[] data = [0xFF, 0x00, 0xAA, 0x55];

        // Act
        uint[] linearPixels = MemoryBitmapRenderer.Render(data, 8, 4, MemoryBitmapVideoMode.Linear1Bpp, palette);
        uint[] cgaPixels = MemoryBitmapRenderer.Render(data, 8, 4, MemoryBitmapVideoMode.Cga2Color, palette);

        // Assert
        linearPixels.Should().NotEqual(cgaPixels);
    }

    [Fact]
    public void EstimateRequiredBytes_CorrectForAllModes() {
        // Arrange & Act & Assert
        MemoryBitmapRenderer.EstimateRequiredBytes(MemoryBitmapVideoMode.Raw8Bpp, 320, 200).Should().Be(64000);
        MemoryBitmapRenderer.EstimateRequiredBytes(MemoryBitmapVideoMode.Vga256Color, 320, 200).Should().Be(64000);
        MemoryBitmapRenderer.EstimateRequiredBytes(MemoryBitmapVideoMode.VgaModeX, 320, 240).Should().Be(76800);
        MemoryBitmapRenderer.EstimateRequiredBytes(MemoryBitmapVideoMode.Ega16Color, 640, 350).Should().Be(112000);
        MemoryBitmapRenderer.EstimateRequiredBytes(MemoryBitmapVideoMode.Packed4Bpp, 320, 200).Should().Be(32000);
        MemoryBitmapRenderer.EstimateRequiredBytes(MemoryBitmapVideoMode.Cga4Color, 320, 200).Should().Be(16000);
        MemoryBitmapRenderer.EstimateRequiredBytes(MemoryBitmapVideoMode.Cga2Color, 640, 200).Should().Be(16000);
        MemoryBitmapRenderer.EstimateRequiredBytes(MemoryBitmapVideoMode.Linear1Bpp, 640, 480).Should().Be(38400);
        MemoryBitmapRenderer.EstimateRequiredBytes(MemoryBitmapVideoMode.Text, 80, 25).Should().Be(4000);
    }
}
