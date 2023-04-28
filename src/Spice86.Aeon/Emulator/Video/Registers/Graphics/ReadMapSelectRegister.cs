namespace Spice86.Aeon.Emulator.Video.Registers.Graphics;

public class ReadMapSelectRegister {
    public byte Value { get; set; }

    /// <summary>
    /// This field specifies the display memory plane for Read mode 0.
    /// </summary>
    public byte PlaneSelect {
        get => Value;
        set => Value = (byte)(value & 3);
    }
}