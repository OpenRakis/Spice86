namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Abstraction over the destination of force-feedback / rumble
/// requests. Implemented in the UI layer by an SDL-backed adapter
/// that forwards effects to <c>SDL_GameControllerRumble</c> /
/// <c>SDL_HapticRumblePlay</c>. A null implementation is used in
/// headless mode and in tests.
/// </summary>
public interface IRumbleSink {

    /// <summary>
    /// Whether the underlying device supports rumble. The Core uses
    /// this to skip the DOSBox-Staging-equivalent log lines when no
    /// haptic device is connected.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Plays a single rumble effect on the given stick slot
    /// (0 = stick A, 1 = stick B). Implementations must be
    /// non-blocking and tolerate being called from the emulator
    /// thread.
    /// </summary>
    /// <param name="stickIndex">Zero-based stick index (0 or 1).</param>
    /// <param name="effect">Effect to play. Pass
    /// <see cref="RumbleEffect.Stop"/> to cancel the active effect.</param>
    void Play(int stickIndex, RumbleEffect effect);
}
