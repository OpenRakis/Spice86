namespace Spice86.Emulator.Devices.Video;

using Serilog;

using Spice86.Emulator.VM;

/// <summary>
/// VGA Digital Analog Converter Implementation.
/// </summary>
public class VgaDac {
    public const int VgaDacnotInitialized = 0;
    public const int VgaDacRead = 1;
    public const int VgaDacWrite = 2;
    private const int BlueIndex = 2;
    private const int GreenIndex = 1;
    private const int Redindex = 0;
    private readonly Machine _machine;
    private readonly Rgb[] _rgbs = new Rgb[256];
    /* 0 = red, 1 = green, 2 = blue */
    private int _colour;
    private int _readIndex;
    private int _state = 1;
    private int _writeIndex;

    public VgaDac(Machine machine) {
        _machine = machine;

        // Initial VGA default palette initialization
        for (int i = 0; i < _rgbs.Length; i++) {
            Rgb rgb = new();
            rgb.R = (byte)(((i >> 5) & 0x7) * 255 / 7);
            rgb.G = (byte)(((i >> 2) & 0x7) * 255 / 7);
            rgb.B = (byte)((i & 0x3) * 255 / 3);
            _rgbs[i] = rgb;
        }
    }

    public static byte From8bitTo6bitColor(byte color8bit) => (byte)((uint)color8bit >> 2);
 
    public static byte From6bitColorTo8bit(byte color6bit) => (byte)((color6bit & 0b111111) << 2);

    public int GetColour() {
        return _colour;
    }

    public int GetReadIndex() {
        return _readIndex;
    }

    public Rgb[] GetRgbs() {
        return _rgbs;
    }

    public int GetState() {
        return _state;
    }

    public int GetWriteIndex() {
        return _writeIndex;
    }

    public byte ReadColor() {
        Rgb rgb = _rgbs[_readIndex];
        byte value = _colour switch {
            Redindex => rgb.R,
            GreenIndex => rgb.G,
            BlueIndex => rgb.B,
            _ => throw new InvalidColorIndexException(_machine, _colour)
        };
        _colour = (_colour + 1) % 3;
        if (_colour == 0) {
            _writeIndex++;
        }
        return value;
    }

    public void SetColour(int colour) {
        this._colour = colour;
    }

    public void SetReadIndex(int readIndex) {
        this._readIndex = readIndex;
    }

    public void SetState(int state) {
        this._state = state;
    }

    public void SetWriteIndex(int writeIndex) {
        this._writeIndex = writeIndex;
    }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    public void WriteColor(byte colorValue) {
        Rgb rgb = _rgbs[_writeIndex];
        switch (_colour) {
            case Redindex:
                rgb.R = colorValue;
                break;
            case GreenIndex:
                rgb.G = colorValue;
                break;
            case BlueIndex:
                rgb.B = colorValue;
                break;
            default:
                throw new InvalidColorIndexException(_machine, _colour);
        }

        _colour = (_colour + 1) % 3;
        if (_colour == 0) {
            _writeIndex++;
        }
    }
}