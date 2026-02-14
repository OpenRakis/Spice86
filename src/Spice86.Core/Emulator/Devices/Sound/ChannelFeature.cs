// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

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
