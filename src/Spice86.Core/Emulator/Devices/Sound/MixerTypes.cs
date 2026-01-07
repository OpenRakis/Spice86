// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Features that a mixer channel can have.
/// Mirrors DOSBox Staging's ChannelFeature enum.
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
/// Filter state - on or off.
/// Mirrors DOSBox Staging's FilterState enum.
/// </summary>
public enum FilterState {
    /// <summary>
    /// Filter is disabled.
    /// </summary>
    Off,
    
    /// <summary>
    /// Filter is enabled.
    /// </summary>
    On
}

/// <summary>
/// Represents a line index in the audio output (left or right).
/// </summary>
public enum LineIndex {
    Left = 0,
    Right = 1
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
/// Crossfeed effect presets - mirrors DOSBox CrossfeedPreset.
/// Crossfeed mixes a portion of the left channel into the right and vice-versa,
/// creating a more natural stereo image for headphone listening.
/// </summary>
public enum CrossfeedPreset {
    /// <summary>
    /// No crossfeed processing.
    /// </summary>
    None,
    
    /// <summary>
    /// Light crossfeed (20% strength).
    /// </summary>
    Light,
    
    /// <summary>
    /// Normal crossfeed (40% strength) - default.
    /// </summary>
    Normal,
    
    /// <summary>
    /// Strong crossfeed (60% strength).
    /// </summary>
    Strong
}

/// <summary>
/// Reverb effect presets - mirrors DOSBox ReverbPreset.
/// Reverb simulates acoustic reflections in various room sizes.
/// </summary>
public enum ReverbPreset {
    /// <summary>
    /// No reverb processing.
    /// </summary>
    None,
    
    /// <summary>
    /// Tiny room reverb (very short decay).
    /// </summary>
    Tiny,
    
    /// <summary>
    /// Small room reverb.
    /// </summary>
    Small,
    
    /// <summary>
    /// Medium room reverb - default.
    /// </summary>
    Medium,
    
    /// <summary>
    /// Large hall reverb.
    /// </summary>
    Large,
    
    /// <summary>
    /// Huge cathedral-like reverb (long decay).
    /// </summary>
    Huge
}

/// <summary>
/// Chorus effect presets - mirrors DOSBox ChorusPreset.
/// Chorus creates a thicker, richer sound by adding delayed copies with pitch variation.
/// </summary>
public enum ChorusPreset {
    /// <summary>
    /// No chorus processing.
    /// </summary>
    None,
    
    /// <summary>
    /// Light chorus effect.
    /// </summary>
    Light,
    
    /// <summary>
    /// Normal chorus effect - default.
    /// </summary>
    Normal,
    
    /// <summary>
    /// Strong chorus effect.
    /// </summary>
    Strong
}

/// <summary>
/// Resampling method - mirrors DOSBox ResampleMethod.
/// Controls how audio is resampled when channel rate differs from mixer rate.
/// </summary>
public enum ResampleMethod {
    /// <summary>
    /// Use linear interpolation for upsampling, Speex-like for downsampling.
    /// </summary>
    LerpUpsampleOrResample,
    
    /// <summary>
    /// Zero-order hold upsampling followed by resampling (vintage DAC sound).
    /// </summary>
    ZeroOrderHoldAndResample,
    
    /// <summary>
    /// High-quality Speex-like resampling for both up and down.
    /// </summary>
    Resample
}

/// <summary>
/// Mixer operational state - mirrors DOSBox MixerState enum.
/// Controls overall mixer behavior and audio output.
/// </summary>
public enum MixerState {
    /// <summary>
    /// Audio device is not initialized or disabled.
    /// </summary>
    NoSound,
    
    /// <summary>
    /// Audio is actively playing and mixing.
    /// </summary>
    On,
    
    /// <summary>
    /// Audio is muted (device active but producing silence).
    /// </summary>
    Muted
}

/// <summary>
/// Settings for a mixer channel - mirrors DOSBox MixerChannelSettings struct.
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
