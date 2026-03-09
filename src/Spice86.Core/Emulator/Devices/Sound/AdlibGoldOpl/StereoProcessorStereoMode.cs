namespace Spice86.Core.Emulator.Devices.Sound.AdlibGoldOpl;

/// <summary>
///     Describes the stereo matrix applied to the selected source.
/// </summary>
internal enum StereoProcessorStereoMode : byte {
    /// <summary>
    ///     Mixes both channels together into mono.
    /// </summary>
    ForcedMono = 0,

    /// <summary>
    ///     Passes the stereo channels through unchanged.
    /// </summary>
    LinearStereo = 1,

    /// <summary>
    ///     Applies an all-pass filter to create pseudo-stereo depth.
    /// </summary>
    PseudoStereo = 2,

    /// <summary>
    ///     Introduces crosstalk for the spatial stereo effect.
    /// </summary>
    SpatialStereo = 3
}
