namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Selectable OPL backend types.
/// </summary>
public enum OplType {
    /// <summary>
    /// Standard opl as found on the Sound Blaster Pro 2 (default).
    /// </summary>
    SbPro2,

    /// <summary>
    /// AdLib Gold enhanced signal path.
    /// </summary>
    Gold
}