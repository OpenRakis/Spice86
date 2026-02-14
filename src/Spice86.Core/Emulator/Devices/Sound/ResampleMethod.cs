// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
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
