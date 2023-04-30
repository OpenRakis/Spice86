namespace Spice86.Aeon.Emulator.Video;

public class ArgbPalette {
    private readonly byte[,] _sixBitPalette;

    public ArgbPalette(byte[,] sixBitPalette) {
        _sixBitPalette = sixBitPalette;
    }

    public uint this[int index] {
        get {
            uint r = _sixBitPalette[index, 0];
            uint g = _sixBitPalette[index, 1];
            uint b = _sixBitPalette[index, 2];
            uint red = r << 2 | r >> 4;
            uint green = g << 2 | g >> 4;
            uint blue = b << 2 | b >> 4;
            return 0xFF000000U | red << 16 | green << 8 | blue;
        }
        set {
            _sixBitPalette[index, 0] = (byte)(value >> 16 & 0x3F);
            _sixBitPalette[index, 1] = (byte)(value >> 8 & 0x3F);
            _sixBitPalette[index, 2] = (byte)(value & 0x3F);
        }
    }
}