namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
///   A 32 bit representation of an 18-bit color palette.
/// </summary>
public class ArgbPalette {
    private readonly uint[,,] _32BitPalette;

    internal byte[,] SixBytePalette { get; } = new byte[256, 3];

    /// <summary>
    ///    Creates a new instance of the <see cref="ArgbPalette"/> class.
    /// </summary>
    public ArgbPalette() {
        // Pre-calculate 32-bit palette.
        _32BitPalette = new uint[64, 64, 64];
        for (int r = 0; r < 64; r++) {
            for (int g = 0; g < 64; g++) {
                for (int b = 0; b < 64; b++) {
                    int red = r << 2 | r >> 4;
                    int green = g << 2 | g >> 4;
                    int blue = b << 2 | b >> 4;
                    _32BitPalette[r, g, b] = 0xFF000000U | (uint)(red << 16) | (uint)(green << 8) | (uint)blue;
                }
            }
        }
    }

    /// <summary>
    ///    Gets or sets the color at the specified index.
    /// </summary>
    public uint this[int index] {
        get {
            uint r = SixBytePalette[index, 0];
            uint g = SixBytePalette[index, 1];
            uint b = SixBytePalette[index, 2];
            return _32BitPalette[r, g, b];
        }
        set {
            SixBytePalette[index, 0] = (byte)(value >> 16 & 0x3F);
            SixBytePalette[index, 1] = (byte)(value >> 8 & 0x3F);
            SixBytePalette[index, 2] = (byte)(value & 0x3F);
        }
    }
}