namespace Spice86.Shared.Emulator.Input.Joystick.Replay;

/// <summary>
/// One entry in a <see cref="JoystickReplayScript"/>. A step describes
/// a single logical joystick event plus the wall-clock delay (in
/// milliseconds, relative to the previous step) at which it should
/// fire.
/// </summary>
/// <remarks>
/// All payload fields are optional: only the subset relevant to
/// <see cref="Type"/> needs to be set, the others are ignored. This
/// keeps the JSON document hand-editable without forcing union types
/// on the serializer. The player validates the relevant fields per
/// step type and skips invalid entries with a warning.
/// </remarks>
public sealed class JoystickReplayStep {
    /// <summary>
    /// Wall-clock delay before this step fires, in milliseconds,
    /// measured from the firing time of the previous step (or from
    /// the start of the script for the first step). Negative values
    /// are treated as zero by the player.
    /// </summary>
    public double DelayMs { get; set; }

    /// <summary>Step kind; selects which payload fields apply.</summary>
    public JoystickReplayStepType Type { get; set; }

    /// <summary>Stick index (0 = stick A, 1 = stick B). Used by
    /// every step type.</summary>
    public int StickIndex { get; set; }

    /// <summary>Axis identifier; only meaningful when
    /// <see cref="Type"/> is <see cref="JoystickReplayStepType.Axis"/>.</summary>
    public JoystickAxis Axis { get; set; }

    /// <summary>Axis value in the range [-1, 1]; only meaningful
    /// when <see cref="Type"/> is
    /// <see cref="JoystickReplayStepType.Axis"/>.</summary>
    public float Value { get; set; }

    /// <summary>Button index (0..3); only meaningful when
    /// <see cref="Type"/> is
    /// <see cref="JoystickReplayStepType.Button"/>.</summary>
    public int ButtonIndex { get; set; }

    /// <summary>True for press, false for release; only meaningful
    /// when <see cref="Type"/> is
    /// <see cref="JoystickReplayStepType.Button"/>.</summary>
    public bool Pressed { get; set; }

    /// <summary>Hat direction; only meaningful when
    /// <see cref="Type"/> is
    /// <see cref="JoystickReplayStepType.Hat"/>.</summary>
    public JoystickHatDirection Direction { get; set; }

    /// <summary>Friendly device name reported on connect; only
    /// meaningful when <see cref="Type"/> is
    /// <see cref="JoystickReplayStepType.Connect"/>. Empty when
    /// unset.</summary>
    public string DeviceName { get; set; } = string.Empty;
}
