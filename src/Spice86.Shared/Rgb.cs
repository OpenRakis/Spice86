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
    

    public uint ToBgra() => (uint)(B8 << 16 | G8 << 8 | R8) | 0x000000FF;

    public uint ToArgb() => 0xFF000000 | (uint)R8 << 16 | (uint)G8 << 8 | B8;

    public override string ToString() => System.Text.Json.JsonSerializer.Serialize(this);

    public static implicit operator uint(Rgb v) => v.ToUint();

    public uint ToUint() => ToRgb();

    private uint ToRgb() => (uint)(R8 << 24 | G8 << 16 | B8 << 8) | 0xFF;

    public byte Read(int readChannel) {
        return readChannel switch {
            0 => R6,
            1 => G6,
            2 => B6,
            _ => 0
        };
    }

    public void Write(byte value, int writeChannel) {
        switch (writeChannel)
        {
            // value * 255 / 63, or else colors are way too dark on screen
            // We could shift by 2 instead, but while it's faster,
            // it may not be as accurate.
            case 0:
                R6 = value;
                R8 = (byte)(value * 255 / 63);
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