namespace Spice86.Shared;

/// <summary>
/// RGB representation of a color.
/// </summary>
public class Rgb {
    public byte R { get; set; }

    public byte G { get; set; }

    public byte B { get; set; }

    public uint ToRgba() {
        return (uint)(R << 16 | G << 8 | B) | 0x000000FF;
    }

    public uint ToBgra() {
        return (uint)(B << 16 | G << 8 | R) | 0x000000FF;
    }

    public uint ToArgb() {
        return 0xFF000000 | (uint)R << 16 | (uint)G << 8 | B;
    }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    public static implicit operator uint(Rgb v) {
        return ToUint(v);
    }

    public static uint ToUint(Rgb v) {
        return (uint)(v.R << 16 | v.G << 8 | v.B);
    }
}