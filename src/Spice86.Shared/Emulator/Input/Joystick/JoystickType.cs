namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Logical IBM PC gameport joystick personality, mirroring the
/// <c>joysticktype</c> values supported by DOSBox Staging
/// (see <c>src/hardware/input/joystick.cpp</c>).
/// </summary>
/// <remarks>
/// The type controls how raw stick axes, buttons and hats are
/// projected onto the four axis-decay timers and four button bits
/// of I/O port <c>0x201</c>.
/// </remarks>
public enum JoystickType {
    /// <summary>
    /// No joystick is connected. Port <c>0x201</c> always reads
    /// <c>0xFF</c>. Equivalent to DOSBox Staging <c>JOY_DISABLED</c>.
    /// </summary>
    None,

    /// <summary>
    /// Auto-detect at startup based on the connected SDL device.
    /// Equivalent to DOSBox Staging <c>JOY_AUTO</c>.
    /// </summary>
    Auto,

    /// <summary>
    /// Two independent two-axis sticks, two buttons each.
    /// Equivalent to DOSBox Staging <c>JOY_2AXIS</c>.
    /// </summary>
    TwoAxis,

    /// <summary>
    /// One four-axis stick whose four axes are reported on stick 0
    /// (axes 0..3). Equivalent to DOSBox Staging <c>JOY_4AXIS</c>.
    /// </summary>
    FourAxis,

    /// <summary>
    /// One four-axis stick whose four axes are reported on stick 1
    /// (axes 0..3). Equivalent to DOSBox Staging <c>JOY_4AXIS_2</c>.
    /// </summary>
    FourAxis2,

    /// <summary>
    /// Thrustmaster Flight Control System: stick 0 reports the main
    /// two axes, the hat (POV) is folded into stick 1 axis Y plus
    /// button 4. Equivalent to DOSBox Staging <c>JOY_FCS</c>.
    /// </summary>
    Fcs,

    /// <summary>
    /// CH Flightstick: hat (POV) directions are encoded across the
    /// four button lines via timed pulses, allowing six logical
    /// buttons to be reported. Equivalent to DOSBox Staging
    /// <c>JOY_CH</c>.
    /// </summary>
    Ch,

    /// <summary>
    /// The joystick is hidden from DOS (port <c>0x201</c> reads
    /// <c>0xFF</c>) but its inputs remain available to the mapper UI
    /// and to MCP tooling. Equivalent to DOSBox Staging
    /// <c>JOY_ONLY_FOR_MAPPING</c>.
    /// </summary>
    HiddenForMapping
}
