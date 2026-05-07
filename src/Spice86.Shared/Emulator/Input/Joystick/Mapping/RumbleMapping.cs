namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>
/// Per-controller rumble settings, mirroring the simple effect
/// catalog DOSBox Staging exposes when forwarding game haptics to a
/// connected SDL game controller.
/// </summary>
public sealed class RumbleMapping {

    /// <summary>
    /// Whether rumble is enabled for this profile. When false,
    /// effect requests are silently dropped (still logged at
    /// verbose level so the UI can surface "rumble suppressed").
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Global amplitude scale in <c>[0, 1]</c> applied to every
    /// effect's low- and high-frequency motors before sending to
    /// SDL. Lets the user tame an over-strong controller without
    /// editing individual effects.
    /// </summary>
    public double AmplitudeScale { get; set; } = 1.0;
}
