// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Audio.Common;
using Spice86.Audio.Filters;

/// <summary>
/// Features that a mixer channel can have.
/// </summary>
public enum ChannelFeature {
    /// <summary>
    /// Channel can send to chorus effect.
    /// </summary>
    ChorusSend,

    /// <summary>
    /// Channel contains digital audio (PCM).
    /// </summary>
    DigitalAudio,

    /// <summary>
    /// Channel supports fade-out when stopping.
    /// </summary>
    FadeOut,

    /// <summary>
    /// Channel has a noise gate processor.
    /// </summary>
    NoiseGate,

    /// <summary>
    /// Channel can send to reverb effect.
    /// </summary>
    ReverbSend,

    /// <summary>
    /// Channel can sleep when inactive to save CPU.
    /// </summary>
    Sleep,

    /// <summary>
    /// Channel produces stereo audio.
    /// </summary>
    Stereo,

    /// <summary>
    /// Channel is a synthesizer (OPL, MT-32, etc).
    /// </summary>
    Synthesizer
}

/// <summary>
/// Defines how stereo channels map to output lines.
/// </summary>
public struct StereoLine {
    public LineIndex Left;
    public LineIndex Right;

    public static readonly StereoLine StereoMap = new() { Left = LineIndex.Left, Right = LineIndex.Right };
    public static readonly StereoLine ReverseMap = new() { Left = LineIndex.Right, Right = LineIndex.Left };

    public readonly bool Equals(StereoLine other) {
        return Left == other.Left && Right == other.Right;
    }

    public override readonly bool Equals(object? obj) {
        return obj is StereoLine other && Equals(other);
    }

    public override readonly int GetHashCode() {
        return HashCode.Combine(Left, Right);
    }

    public static bool operator ==(StereoLine left, StereoLine right) {
        return left.Equals(right);
    }

    public static bool operator !=(StereoLine left, StereoLine right) {
        return !left.Equals(right);
    }
}

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
