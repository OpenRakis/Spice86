namespace Spice86.Shared;

/// <summary>
/// RGB representation of a color.
/// </summary>
public class Rgb {
    /// <summary>
    /// Red channel, on 8 bit
    /// </summary>
    public byte R8 { get; set; }

    /// <summary>
    /// Green channel, on 8 bit
    /// </summary>
    public byte G8 { get; set; }

    /// <summary>
    /// Blue channel, on 8 bit
    /// </summary>
    public byte B8 { get; set; }
    
    
    /// <summary>
    /// Red channel, on 6 bit
    /// </summary>
    public byte R6 { get; set; }

    /// <summary>
    /// Green channel, on 6 bit
    /// </summary>
    public byte G6 { get; set; }

    /// <summary>
    /// Blue channel, on 6 bit
    /// </summary>
    public byte B6 { get; set; }

    public uint ToRgba() => 0x000000FF | ToRgb();
    

    public uint ToBgra() => (uint)(B8 << 24 | G8 << 16 | R8 << 8 ) | 0x000000FF;

    public uint ToArgb() => 0xFF000000 | (uint)R8 << 16 | (uint)G8 << 8 | B8;

    public override string ToString() => System.Text.Json.JsonSerializer.Serialize(this);

    public static implicit operator uint(Rgb v) => v.ToUint();

    public uint ToUint() => ToRgb();

    public uint ToRgb() => (uint)(R8 << 24 | G8 << 16 | B8 << 8) | 0x000000FF;

    public byte Read(ref int readChannel, ref byte readIndex) {
        switch (readChannel) {
            default:
                readChannel = 0;
                readIndex++;
                return R6;
            case 1:
                return G6;
            case 2:
                return B6;
        }
    }

    public void Write(byte value, ref int writeChannel, ref byte writeIndex) {
        switch (writeChannel)
        {
            // value * 255 / 63, or else colors are way too dark on screen
            // We could shift by 2 instead, but while it's faster,
            // it may not be as accurate.
            default:
                R6 = value;
                R8 = (byte)(value * 255 / 63);
                writeChannel = 0;
                writeIndex++;
                break;
            case 1:
                G6 = value;
                G8 = (byte)(value * 255 / 63);
                break;
            case 2:
                B6 = value;
                B8 = (byte)(value * 255 / 63);
                break;
        }
    }
}