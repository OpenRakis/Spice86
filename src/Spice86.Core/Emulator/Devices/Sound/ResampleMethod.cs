namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
///     Specifies the method used to resample audio from the channel sample rate to the mixer rate.
/// </summary>
public enum ResampleMethod {
    /// <summary>
    ///     Linear interpolation for upsampling. If downsampling is needed, a more advanced resampler
    ///     would be used (currently falls back to simple resampling).
    /// </summary>
    LerpUpsampleOrResample,

    /// <summary>
    ///     Zero-order-hold upsampling to a target frequency, followed by advanced resampling to the mixer rate.
    ///     This emulates the metallic, crunchy sound of old DACs.
    /// </summary>
    ZeroOrderHoldAndResample,

    /// <summary>
    ///     High-quality resampling directly from the channel sample rate to the mixer rate.
    ///     Provides mathematically correct, high-quality output.
    /// </summary>
    Resample
}
