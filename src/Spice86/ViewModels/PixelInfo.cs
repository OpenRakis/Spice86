namespace Spice86.ViewModels;

using Avalonia.Media;

/// <summary>
///     Represents a single pixel in the grid overlay mode of the Memory Bitmap viewer.
///     Contains pixel coordinates, memory address, raw byte value, palette index, ARGB color,
///     and display properties for rendering hex values overlaid on the pixel's color.
/// </summary>
public sealed class PixelInfo {
    /// <summary>
    ///     The pixel X coordinate in the rendered image.
    /// </summary>
    public int X { get; }

    /// <summary>
    ///     The pixel Y coordinate in the rendered image.
    /// </summary>
    public int Y { get; }

    /// <summary>
    ///     The absolute memory address this pixel's data was read from.
    /// </summary>
    public uint MemoryAddress { get; }

    /// <summary>
    ///     The raw byte value at the pixel's memory location.
    /// </summary>
    public byte RawByte { get; }

    /// <summary>
    ///     The palette index used for this pixel (same as RawByte for 8bpp modes).
    /// </summary>
    public int PaletteIndex { get; }

    /// <summary>
    ///     The resolved ARGB color value for this pixel.
    /// </summary>
    public uint ArgbColor { get; }

    /// <summary>
    ///     The hex string displayed on the pixel (e.g. "3F").
    /// </summary>
    public string HexValue { get; }

    /// <summary>
    ///     The background brush matching this pixel's color.
    /// </summary>
    public SolidColorBrush Background { get; }

    /// <summary>
    ///     The foreground brush for text, chosen to contrast against the background.
    /// </summary>
    public SolidColorBrush Foreground { get; }

    /// <summary>
    ///     Multi-line tooltip with full pixel details.
    /// </summary>
    public string TooltipText { get; }

    /// <summary>
    ///     Creates a new PixelInfo from pixel data.
    /// </summary>
    /// <param name="x">X coordinate in the image.</param>
    /// <param name="y">Y coordinate in the image.</param>
    /// <param name="memoryAddress">The absolute memory address this pixel was read from.</param>
    /// <param name="rawByte">The raw byte value at the memory address.</param>
    /// <param name="paletteIndex">The palette index (or raw value for non-indexed modes).</param>
    /// <param name="argbColor">The resolved ARGB color value.</param>
    public PixelInfo(int x, int y, uint memoryAddress, byte rawByte, int paletteIndex, uint argbColor) {
        X = x;
        Y = y;
        MemoryAddress = memoryAddress;
        RawByte = rawByte;
        PaletteIndex = paletteIndex;
        ArgbColor = argbColor;
        HexValue = rawByte.ToString("X2");

        byte r = (byte)((argbColor >> 16) & 0xFF);
        byte g = (byte)((argbColor >> 8) & 0xFF);
        byte b = (byte)(argbColor & 0xFF);
        Background = new SolidColorBrush(Color.FromRgb(r, g, b));

        // Perceived luminance: use white text on dark backgrounds, black on light
        double luminance = 0.299 * r + 0.587 * g + 0.114 * b;
        Foreground = luminance < 128
            ? new SolidColorBrush(Colors.White)
            : new SolidColorBrush(Colors.Black);

        TooltipText = $"Pixel ({x}, {y})\n" +
                      $"Address: {memoryAddress:X5}\n" +
                      $"Byte: 0x{rawByte:X2} ({rawByte})\n" +
                      $"Palette: {paletteIndex}\n" +
                      $"Color: #{argbColor:X8} (R:{r} G:{g} B:{b})";
    }
}
