namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Selectable OPL synthesis modes.
/// Matches DOSBox staging OplMode enum.
/// </summary>
public enum OplMode {
    /// <summary>
    /// No OPL synthesis.
    /// </summary>
    None,

    /// <summary>
    /// Single OPL2 chip (mono, 9 channels).
    /// </summary>
    Opl2,

    /// <summary>
    /// Dual OPL2 chips (stereo, 18 channels).
    /// </summary>
    DualOpl2,

    /// <summary>
    /// OPL3 chip (stereo, 18 channels, 4-op modes).
    /// </summary>
    Opl3,

    /// <summary>
    /// OPL3 with AdLib Gold enhanced signal path.
    /// </summary>
    Opl3Gold
}