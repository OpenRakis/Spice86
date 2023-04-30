namespace Spice86.Aeon.Emulator.Video.Registers.AttributeController;

public sealed class ColorPlaneEnableRegister : VgaRegisterBase {
    private byte _value;

    public override byte Value {
        get => _value;
        set {
            _value = value;
            for (int i = 0; i < 4; i++) {
                PlanesEnabled[i] = (value & 1 << i) != 0;
            }
        }
    }

    public ColorPlaneEnableRegister() {
        PlanesEnabled = new bool[4];
    }

    public bool[] PlanesEnabled { get; }
}