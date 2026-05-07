namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>
/// Mapping of one raw physical button (e.g. an SDL button index on a
/// specific device) to a logical <see cref="VirtualButton"/>.
/// </summary>
public sealed class ButtonMapping {

    /// <summary>
    /// Zero-based index of the raw button on the source device, as
    /// reported by SDL (<c>SDL_JoystickGetButton</c>).
    /// </summary>
    public int RawButtonIndex { get; set; }

    /// <summary>
    /// Logical destination button on the gameport.
    /// </summary>
    public VirtualButton Target { get; set; } = VirtualButton.None;

    /// <summary>
    /// Whether the press should be reported as a single one-shot
    /// pulse (auto-fire / debounced) rather than tracking the raw
    /// press state. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AutoFire { get; set; }
}
