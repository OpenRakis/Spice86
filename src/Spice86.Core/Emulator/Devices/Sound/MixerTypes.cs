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
