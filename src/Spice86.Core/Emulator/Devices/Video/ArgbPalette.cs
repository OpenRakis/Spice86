namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.InternalDebugger;

/// <summary>
///   A 32 bit representation of an 18-bit color palette.
/// </summary>
public class ArgbPalette : IDebuggableComponent {
    private readonly byte[,] _sixBitPalette;
    private readonly uint[,,] _32BitPalette;

    /// <summary>
    ///    Creates a new instance of the <see cref="ArgbPalette"/> class.
    /// </summary>
    /// <param name="sixBitPalette">The VGA palette in 256 6-bit rgb triplets</param>
    public ArgbPalette(byte[,] sixBitPalette) {
        _sixBitPalette = sixBitPalette;
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
            uint r = _sixBitPalette[index, 0];
            uint g = _sixBitPalette[index, 1];
            uint b = _sixBitPalette[index, 2];
            return _32BitPalette[r, g, b];
        }
        set {
            _sixBitPalette[index, 0] = (byte)(value >> 16 & 0x3F);
            _sixBitPalette[index, 1] = (byte)(value >> 8 & 0x3F);
            _sixBitPalette[index, 2] = (byte)(value & 0x3F);
        }
    }

    /// <inheritdoc/>
    public void Accept<T>(T emulatorDebugger) where T : IInternalDebugger {
        emulatorDebugger.Visit(this);
    }
}