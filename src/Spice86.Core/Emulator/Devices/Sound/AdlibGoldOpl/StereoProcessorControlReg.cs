// SPDX-FileCopyrightText: 2022-2025 The DOSBox Staging Team

namespace Spice86.Core.Emulator.Devices.Sound.AdlibGoldOpl;

/// <summary>
///     Enumerates the stereo processor control registers exposed over the AdLib Gold I/O interface.
///     Reference: enum class StereoProcessorControlReg in DOSBox adlib_gold.h
/// </summary>
public enum StereoProcessorControlReg {
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