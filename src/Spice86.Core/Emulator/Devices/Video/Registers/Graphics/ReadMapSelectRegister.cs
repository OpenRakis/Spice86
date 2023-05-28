namespace Spice86.Core.Emulator.Devices.Video.Registers.Graphics;

public class ReadMapSelectRegister : Register8 {
    /// <summary>
    ///     This field specifies the display memory plane for Read mode 0.
    /// </summary>
    public byte PlaneSelect {
        get => GetBits(1, 0);
        set => SetBits(1, 0, value);
    }
}