namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Audio.Common;

/// <summary>
/// Used to specify software mixer level channel settings
/// </summary>
/// <remarks>
/// 2022-2025 The DOSBox Staging Team
/// </remarks>
public struct SoundChannelSettings {
    /// <summary>
    /// Whether the channel is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// User-controlled volume gain.
    /// </summary>
    public AudioFrame UserVolumeGain { get; set; }

    /// <summary>
    /// Output line mapping (stereo/reverse/etc).
    /// </summary>
    public StereoLine LineoutMap { get; set; }

    /// <summary>
    /// Crossfeed strength (0.0 to 1.0).
    /// </summary>
    public float CrossfeedStrength { get; set; }

    /// <summary>
    /// Reverb send level (0.0 to 1.0).
    /// </summary>
    public float ReverbLevel { get; set; }

    /// <summary>
    /// Chorus send level (0.0 to 1.0).
    /// </summary>
    public float ChorusLevel { get; set; }
}
