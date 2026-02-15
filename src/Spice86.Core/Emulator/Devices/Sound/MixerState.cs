// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
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
