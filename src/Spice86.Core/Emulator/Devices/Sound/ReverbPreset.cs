// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
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
