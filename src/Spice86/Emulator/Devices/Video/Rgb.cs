namespace Spice86.Emulator.Devices.Video;

/// <summary>
/// RGB representation of a color.
/// </summary>
public class Rgb
{
    private int r;
    private int g;
    private int b;
    public int GetR()
    {
        return r;
    }

    public void SetR(int r)
    {
        this.r = r;
    }

    public int GetG()
    {
        return g;
    }

    public void SetG(int g)
    {
        this.g = g;
    }

    public int GetB()
    {
        return b;
    }

    public void SetB(int b)
    {
        this.b = b;
    }

    public int ToArgb()
    {
        return (int)(0xff000000 | (r << 16) | (uint)(g << 8) | (uint)b);
    }

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}
