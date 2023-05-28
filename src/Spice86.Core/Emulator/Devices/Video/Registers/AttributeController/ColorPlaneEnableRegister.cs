namespace Spice86.Core.Emulator.Devices.Video.Registers.AttributeController;

public sealed class ColorPlaneEnableRegister : Register8 {
    private byte _value;

    public ColorPlaneEnableRegister() {
        PlanesEnabled = new bool[4];
    }

    public override byte Value {
        get => _value;
        set {
            _value = value;
            for (int i = 0; i < 4; i++) {
                PlanesEnabled[i] = (value & 1 << i) != 0;
            }
        }
    }

    public bool[] PlanesEnabled { get; }
}