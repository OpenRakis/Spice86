namespace Spice86.Emulator.Devices.Video;

/// <summary>
/// RGB representation of a color.
/// </summary>
public class Rgb {
    private byte r;
    private byte g;
    private byte b;

    public byte GetR() {
        return r;
    }
    public byte GetG() {
        return g;
    }
    public byte GetB() {
        return b;
    }

    public void SetR(byte r) {
        this.r = r;
    }

    public void SetG(byte g) {
        this.g = g;
    }

    public void SetB(byte b) {
        this.b = b;
    }

    public uint ToArgb() {
        return (0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b);
    }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}