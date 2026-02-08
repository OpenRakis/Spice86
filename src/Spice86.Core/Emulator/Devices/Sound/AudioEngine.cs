namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Available audio engines.
/// </summary>
public enum AudioEngine {
    /// <summary>
    /// Cross-platform audio backend using WASAPI on Windows and SDL on other platforms.
    /// This is the recommended and default audio engine.
    /// </summary>
    CrossPlatform,

    /// <summary>
    /// Dummy audio engine that produces no sound.
    /// Useful for testing or when no audio output is needed.
    /// </summary>
    Dummy
}