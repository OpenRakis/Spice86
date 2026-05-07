namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Single force-feedback / rumble effect, modelled on
/// <c>SDL_GameControllerRumble</c> (low-frequency / high-frequency
/// motor amplitudes plus a duration). Matches the simple rumble
/// catalog used by DOSBox Staging when forwarding game-triggered
/// haptics to a connected controller.
/// </summary>
/// <param name="LowFrequencyAmplitude">Strength of the low-frequency
/// (heavy) motor in <c>[0.0, 1.0]</c>.</param>
/// <param name="HighFrequencyAmplitude">Strength of the
/// high-frequency (light) motor in <c>[0.0, 1.0]</c>.</param>
/// <param name="DurationMilliseconds">How long the effect should
/// play, in milliseconds. 0 stops any active effect.</param>
public readonly record struct RumbleEffect(
    float LowFrequencyAmplitude,
    float HighFrequencyAmplitude,
    int DurationMilliseconds) {

    /// <summary>
    /// Stops any currently playing rumble effect.
    /// </summary>
    public static RumbleEffect Stop { get; } = new(0f, 0f, 0);
}
