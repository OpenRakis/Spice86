namespace Spice86.Aeon.Emulator.Video.Registers.Sequencer;

public sealed class PlaneMaskRegister : VgaRegisterBase {
    private byte _value;

    public override byte Value {
        get => _value;
        set {
            _value = value;
            for (int i = 0; i < 8; i++) {
                PlanesEnabled[i] = (value & 1 << i) != 0;
            }
        }
    }

    public PlaneMaskRegister() {
        PlanesEnabled = new bool[8];
    }

    public bool[] PlanesEnabled { get; }

    [Obsolete("old aeon code")]
    public MaskValue MaskValue {
        get => Value;
        set => Value = value.Packed;
    }
}