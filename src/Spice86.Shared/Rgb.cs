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

    public static implicit operator uint(Rgb v) => ToUint(v);

    public static uint ToUint(Rgb v) => v.ToRgb();

    private uint ToRgb() => (uint)(R8 << 16 | G8 << 8 | B8);
}