namespace Spice86.Core.Emulator.Devices.Sound.AdlibGoldOpl;

/// <summary>
///     Identifies the signal source selected by the stereo processor.
/// </summary>
/// <remarks>
/// 2022-2025 The DOSBox Staging Team
/// </remarks>
internal enum StereoProcessorSourceSelector : byte {
    /// <summary>
    ///     Uses input channel A, position 1.
    /// </summary>
    SoundA1 = 2,

    /// <summary>
    ///     Uses input channel A, position 2.
    /// </summary>
    SoundA2 = 3,

    /// <summary>
    ///     Uses input channel B, position 1.
    /// </summary>
    SoundB1 = 4,

    /// <summary>
    ///     Uses input channel B, position 2.
    /// </summary>
    SoundB2 = 5,

    /// <summary>
    ///     Uses the primary stereo input.
    /// </summary>
    Stereo1 = 6,

    /// <summary>
    ///     Uses the secondary stereo input.
    /// </summary>
    Stereo2 = 7
}
