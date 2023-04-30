namespace Spice86.Aeon.Emulator.Video.Registers;

public class DacRegisters {
    private int _indexRegister;
    private int _internalIndex;
    private int _tripletCounter;
    private readonly byte[,] _palette = new byte[256, 3];

    public DacRegisters() {
        ArgbPalette = new ArgbPalette(_palette);
    }

    /// <summary>
    /// Which bits to use for the palette address.
    /// </summary>
    public byte PixelMask { get; set; }

    /// <summary>
    /// Status bits indicate the I/O address of the last CPU write to the external DAC/Color Palette:
    /// 0: The last write was to 3C8h (write mode)
    /// 3: The last write was to 3C7h (read mode)
    /// </summary>
    public byte State { get; private set; }

    /// <summary>
    /// Contains the index of Color Palette entry to be read.
    /// </summary>
    public byte IndexRegisterReadMode {
        get => (byte)_indexRegister;
        set {
            _indexRegister = value + 1;
            _internalIndex = value;
            _tripletCounter = 0;
        }
    }

    /// <summary>
    /// Contains the index of Color Palette entry to be written.
    /// </summary>
    public byte IndexRegisterWriteMode {
        get => (byte)_indexRegister;
        set {
            _indexRegister = value;
            _internalIndex = value;
            _tripletCounter = 0;
        }
    }

    /// <summary>
    /// Register to write or read bytes from the Color Palette.
    /// </summary>
    public byte DataRegister {
        get {
            byte result = _palette[_internalIndex, _tripletCounter++];
            if (_tripletCounter == 3) {
                _indexRegister++;
                _internalIndex++;
                _tripletCounter = 0;
            }
            State = 3;
            return result;
        }
        set {
            _palette[_internalIndex, _tripletCounter++] = (byte)(value & 0x3F);
            if (_tripletCounter == 3) {
                _indexRegister++;
                _internalIndex++;
                _tripletCounter = 0;
            }
            State = 0;
        }
    }

    /// <summary>
    /// Converts the internal palette to an array of ARGB values.
    /// </summary>
    public ArgbPalette ArgbPalette { get; }
}