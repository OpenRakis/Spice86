namespace Spice86.Emulator.Devices.Video;

using Spice86.Emulator.Machine;

/// <summary>
/// VGA Digital Analog Converter Implementation.
/// </summary>
public class VgaDac
{
    public const int VgaDacnotInitialized = 0;
    public const int VgaDacRead = 1;
    public const int VgaDacWrite = 2;
    private const int BlueIndex = 2;
    private const int GreenIndex = 1;
    private const int Redindex = 0;
    private readonly Machine _machine;
    private readonly Rgb[] _rgbs = new Rgb[256];
    private int _colour;
    private int _readIndex;
    private int _state = 1;
    /* 0 = red, 1 = green, 2 = blue */
    private int _writeIndex;

    public VgaDac(Machine machine)
    {
        _machine = machine;

        // Initial VGA default palette initialization
        for (int i = 0; i < _rgbs.Length; i++)
        {
            Rgb rgb = new();
            rgb.SetR((((i >> 5) & 0x7) * 255 / 7));
            rgb.SetG((((i >> 2) & 0x7) * 255 / 7));
            rgb.SetB(((i & 0x3) * 255 / 3));
            _rgbs[i] = rgb;
        }
    }

    public static int From6bitColorTo8bit(int color6bit) => (byte)((color6bit & 0b111111) << 2);

    public static int From8bitTo6bitColor(int color8bit) => (byte)((uint)color8bit >> 2);

    public int GetColour()
    {
        return _colour;
    }

    public int GetReadIndex()
    {
        return _readIndex;
    }

    public Rgb[] GetRgbs()
    {
        return _rgbs;
    }

    public int GetState()
    {
        return _state;
    }

    public int GetWriteIndex()
    {
        return _writeIndex;
    }

    public int ReadColor()
    {
        Rgb rgb = _rgbs[_readIndex];
        int value = _colour switch
        {
            Redindex => rgb.GetR(),
            GreenIndex => rgb.GetG(),
            BlueIndex => rgb.GetB(),
            _ => throw new InvalidColorIndexException(_machine, _colour)
        };
        _colour = (_colour + 1) % 3;
        if (_colour == 0)
        {
            _writeIndex++;
        }
        return value;
    }

    public void SetColour(int colour)
    {
        this._colour = colour;
    }

    public void SetReadIndex(int readIndex)
    {
        this._readIndex = readIndex;
    }

    public void SetState(int state)
    {
        this._state = state;
    }

    public void SetWriteIndex(int writeIndex)
    {
        this._writeIndex = writeIndex;
    }

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    public void WriteColor(int colorValue)
    {
        Rgb rgb = _rgbs[_writeIndex];
        if (_colour == Redindex)
        {
            rgb.SetR(colorValue);
        }
        else if (_colour == GreenIndex)
        {
            rgb.SetG(colorValue);
        }
        else if (_colour == BlueIndex)
        {
            rgb.SetB(colorValue);
        }
        else
        {
            throw new InvalidColorIndexException(_machine, _colour);
        }

        _colour = (_colour + 1) % 3;
        if (_colour == 0)
        {
            _writeIndex++;
        }
    }
}