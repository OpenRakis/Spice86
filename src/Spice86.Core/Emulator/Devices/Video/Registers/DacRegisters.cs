namespace Spice86.Core.Emulator.Devices.Video.Registers;

using System.Diagnostics;

/// <summary>
/// Represents the registers of the video DAC.
/// </summary>
public class DacRegisters {
    private int _indexRegister;
    private byte _internalIndex;
    private int _tripletCounter;
    private byte _pixelMask = 0xFF;

    /// <summary>
    /// The DAC Palette, represented as a 256 * 3 array. Stores the current set of colors.
    /// </summary>
    public readonly byte[,] Palette = new byte[256, 3];

    /// <summary>
    /// Precomputed ARGB pixel values for 8-bit (256-colour) mode. Index is the direct DAC
    /// palette index; value is the packed 0xAARRGGBB pixel with <see cref="PixelMask"/> already applied.
    /// Rebuilt automatically whenever the DAC palette or <see cref="PixelMask"/> changes.
    /// </summary>
    public readonly uint[] PaletteMap = new uint[256];

    /// <summary>
    /// Precomputed ARGB pixel values for 4-bit attribute-indexed modes (EGA, CGA, text).
    /// Index is the 4-bit attribute value (0–15); value is the final packed pixel after the
    /// InternalPalette + ColorSelect register translation and <see cref="PixelMask"/> masking.
    /// Rebuilt by calling <see cref="RebuildAttributeMap"/> whenever any relevant attribute
    /// register changes.
    /// </summary>
    public readonly uint[] AttributeMap = new uint[16];

    /// <summary>
    /// Set to <c>true</c> after a full RGB triplet has been written via <see cref="DataRegister"/>.
    /// Cleared at the start of each write. Consumers (e.g. <c>VgaIoPortHandler</c>) can test
    /// this flag to decide when to call <see cref="RebuildAttributeMap"/>.
    /// </summary>
    public bool TripletJustCompleted { get; private set; }

    /// <summary>
    /// The palette index whose triplet was most recently completed via <see cref="DataRegister"/>.
    /// Valid only when <see cref="TripletJustCompleted"/> is <c>true</c>.
    /// </summary>
    public byte LastWrittenPaletteIndex { get; private set; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public DacRegisters() {
        ArgbPalette = new ArgbPalette(Palette);
        RebuildAllPaletteMap();
    }

    /// <summary>
    ///     Which bits to use for the palette address.
    ///     Changing this mask rebuilds all 256 <see cref="PaletteMap"/> entries immediately.
    /// </summary>
    public byte PixelMask {
        get => _pixelMask;
        set {
            _pixelMask = value;
            RebuildAllPaletteMap();
        }
    }

    /// <summary>
    ///     Status bits indicate the I/O address of the last CPU write to the external DAC/Color Palette:
    ///     0: The last write was to 3C8h (write mode)
    ///     3: The last write was to 3C7h (read mode)
    /// </summary>
    public byte State { get; private set; }

    /// <summary>
    ///     Contains the index of Color Palette entry to be read.
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
    ///     Contains the index of Color Palette entry to be written.
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
    ///     Register to write or read bytes from the Color Palette.
    /// </summary>
    public byte DataRegister {
        get {
            byte result = Palette[_internalIndex, _tripletCounter++];
            if (_tripletCounter == 3) {
                _indexRegister++;
                _internalIndex++;
                _tripletCounter = 0;
            }
            State = 3;
            return result;
        }
        set {
            TripletJustCompleted = false;
            try {
                Palette[_internalIndex, _tripletCounter++] = (byte)(value & 0x3F);
                if (_tripletCounter == 3) {
                    LastWrittenPaletteIndex = _internalIndex;
                    RebuildPaletteMapEntry(_internalIndex);
                    _indexRegister++;
                    _internalIndex++;
                    _tripletCounter = 0;
                    TripletJustCompleted = true;
                }
                State = 0;
            } catch (Exception e) {
                Debug.WriteLine(e);
                throw;
            }
        }
    }

    /// <summary>
    /// Gets the byte from the <see cref="Palette"/>, pointed at by the _internalIndex and _tripletCounter
    /// </summary>
    public byte DataPeek => Palette[_internalIndex, _tripletCounter];

    /// <summary>
    ///     Converts the internal palette to an array of ARGB values.
    ///     Retained for UI and diagnostic use. Hot rendering paths should use <see cref="PaletteMap"/>
    ///     or <see cref="AttributeMap"/> instead.
    /// </summary>
    public ArgbPalette ArgbPalette { get; }

    /// <summary>
    /// Rebuilds a single entry in <see cref="PaletteMap"/>.
    /// The colour stored at <paramref name="index"/> is the DAC palette colour at
    /// <c>index &amp; <see cref="PixelMask"/></c>, converted to ARGB.
    /// </summary>
    public void RebuildPaletteMapEntry(int index) {
        int maskedIndex = index & _pixelMask;
        byte r6 = Palette[maskedIndex, 0];
        byte g6 = Palette[maskedIndex, 1];
        byte b6 = Palette[maskedIndex, 2];
        PaletteMap[index] = ToArgb(r6, g6, b6);
    }

    /// <summary>
    /// Rebuilds all 256 entries in <see cref="PaletteMap"/>. Call this when
    /// <see cref="PixelMask"/> changes (all mappings are affected).
    /// </summary>
    public void RebuildAllPaletteMap() {
        for (int i = 0; i < 256; i++) {
            RebuildPaletteMapEntry(i);
        }
    }

    /// <summary>
    /// Rebuilds all 16 entries in <see cref="AttributeMap"/> using the current attribute-controller
    /// registers. Call this whenever <see cref="AttributeControllerRegisters.InternalPalette"/>,
    /// <see cref="AttributeControllerRegisters.AttributeControllerModeRegister"/> (VideoOutput45Select),
    /// or <see cref="AttributeControllerRegisters.ColorSelectRegister"/> changes, and also after
    /// any change to <see cref="PaletteMap"/> (DAC write or mask change).
    /// </summary>
    public void RebuildAttributeMap(AttributeControllerRegisters attr) {
        bool videoOutput45Select = attr.AttributeControllerModeRegister.VideoOutput45Select;
        int bits67 = attr.ColorSelectRegister.Bits67 << 6;
        int bits45FromColorSelect = attr.ColorSelectRegister.Bits45 << 4;

        for (int i = 0; i < 16; i++) {
            int fromPaletteRam6Bits = attr.InternalPalette[i];
            int bits0To3 = fromPaletteRam6Bits & 0x0F;
            int bits4And5 = videoOutput45Select ? bits45FromColorSelect : (fromPaletteRam6Bits & 0x30);
            int dacIndex = bits67 | bits4And5 | bits0To3;
            AttributeMap[i] = PaletteMap[dacIndex];
        }
    }

    private static uint ToArgb(byte r6, byte g6, byte b6) {
        uint r8 = (uint)(r6 << 2 | r6 >> 4);
        uint g8 = (uint)(g6 << 2 | g6 >> 4);
        uint b8 = (uint)(b6 << 2 | b6 >> 4);
        return 0xFF000000U | (r8 << 16) | (g8 << 8) | b8;
    }
}