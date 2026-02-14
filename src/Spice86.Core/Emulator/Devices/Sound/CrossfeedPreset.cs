// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
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
