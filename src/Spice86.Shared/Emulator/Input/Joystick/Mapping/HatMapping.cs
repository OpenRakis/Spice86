namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>
/// Mapping of a raw hat (POV) on a physical device to a logical
/// gameport hat slot. Most controllers report a single hat at index
/// 0, so the default constructor's values are usable as-is.
/// </summary>
public sealed class HatMapping {

    /// <summary>
    /// Zero-based index of the raw hat on the source device.
    /// </summary>
    public int RawHatIndex { get; set; }

    /// <summary>
    /// Which logical stick the hat is reported on (0 = stick A,
    /// 1 = stick B). DOSBox Staging's <c>FCS</c> personality folds
    /// the hat into stick B; <c>CH</c> spreads it across the four
    /// button bits.
    /// </summary>
    public int TargetStickIndex { get; set; }

    /// <summary>
    /// Whether the hat is enabled. Disabled hats are ignored even
    /// if the device reports them.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
