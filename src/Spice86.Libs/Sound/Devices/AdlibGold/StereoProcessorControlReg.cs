// SPDX-FileCopyrightText: 2022-2025 The DOSBox Staging Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Libs.Sound.Devices.AdlibGold;

/// <summary>
///     Enumerates the stereo processor control registers exposed over the AdLib Gold I/O interface.
/// </summary>
internal enum StereoProcessorControlReg {
    /// <summary>
    ///     Left channel master volume register.
    /// </summary>
    VolumeLeft,

    /// <summary>
    ///     Right channel master volume register.
    /// </summary>
    VolumeRight,

    /// <summary>
    ///     Bass shelving control register.
    /// </summary>
    Bass,

    /// <summary>
    ///     Treble shelving control register.
    /// </summary>
    Treble,

    /// <summary>
    ///     Source selection and stereo mode register.
    /// </summary>
    SwitchFunctions
}