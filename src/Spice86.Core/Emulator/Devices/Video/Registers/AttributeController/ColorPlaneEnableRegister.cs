namespace Spice86.Core.Emulator.Devices.Video.Registers.AttributeController;

/// <summary>
/// Represents the 8 bit Color Plane Enable register.
/// </summary>
public sealed class ColorPlaneEnableRegister : Register8 {
    private byte _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorPlaneEnableRegister"/> class.
    /// </summary>
    public ColorPlaneEnableRegister() {
        PlanesEnabled = new bool[4];
    }

    ///<inheritdoc/>
    public override byte Value {
        get => _value;
        set {
            _value = value;
            for (int i = 0; i < 4; i++) {
                PlanesEnabled[i] = (value & 1 << i) != 0;
            }
        }
    }

    /// <summary>
    /// Gets the enabled planes.
    /// </summary>
    public bool[] PlanesEnabled { get; }
}