namespace Spice86.Aeon.Emulator.Video.Registers.AttributeController;

public class ColorSelectRegister : VgaRegisterBase {
    /// <summary>
    /// Use these 2 bits as bits 4 and 5 of the color attribute when
    /// <see cref="Spice86.Aeon.Emulator.Video.Registers.AttributeController.AttributeControllerModeRegister.VideoOutput45Select"/>
    /// is set.
    /// </summary>
    public byte Bits45 {
        get => GetBits(1, 0);
        set => SetBits(1, 0, value);
    }

    /// <summary>
    /// Use these 2 bits as bits 6 and 7 of the color attribute, except in mode 13
    /// </summary>
    public byte Bits67 {
        get => GetBits(1, 0);
        set => SetBits(1, 0, value);
    }
}