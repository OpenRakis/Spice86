// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Used to save and restore channel configuration.
/// Reference: DOSBox mixer.h lines 114-121
/// </summary>
public struct MixerChannelSettings {
    /// <summary>
    /// Whether the channel is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// User-controlled volume gain (from MIXER command).
    /// </summary>
    public Spice86.Libs.Sound.Common.AudioFrame UserVolumeGain { get; set; }

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
