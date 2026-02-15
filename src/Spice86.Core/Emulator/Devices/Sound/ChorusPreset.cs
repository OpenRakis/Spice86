// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
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
