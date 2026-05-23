namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>
/// Mapping of one raw physical axis (e.g. an SDL axis index on a
/// specific device) to a logical <see cref="VirtualAxis"/>, with
/// optional inversion, scale and per-axis deadzone override.
/// </summary>
public sealed class AxisMapping {

    /// <summary>
    /// Zero-based index of the raw axis on the source device, as
    /// reported by SDL (<c>SDL_JoystickGetAxis</c>).
    /// </summary>
    public int RawAxisIndex { get; set; }

    /// <summary>
    /// Logical destination axis on the gameport.
    /// </summary>
    public VirtualAxis Target { get; set; } = VirtualAxis.None;

    /// <summary>
    /// Whether the raw axis should be inverted before being sent to
    /// the target axis. Useful for sticks whose Y axis is wired
    /// "up = positive" instead of the DOSBox-Staging convention
    /// "up = negative".
    /// </summary>
    public bool Invert { get; set; }

    /// <summary>
    /// Scaling factor applied after normalization. <c>1.0</c> means
    /// pass-through. Values <c>&gt; 1.0</c> increase sensitivity;
    /// values <c>&lt; 1.0</c> decrease it.
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>
    /// Per-axis deadzone override, expressed as a percentage in
    /// <c>[0, 100]</c>. <see langword="null"/> means "use the
    /// profile-wide deadzone". Mirrors DOSBox Staging's per-axis
    /// deadzone handling.
    /// </summary>
    public int? DeadzonePercent { get; set; }
}
