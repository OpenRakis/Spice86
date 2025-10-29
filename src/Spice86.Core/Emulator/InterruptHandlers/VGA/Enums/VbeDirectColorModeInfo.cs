namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// VBE direct color mode information flags.
/// </summary>
[Flags]
public enum VbeDirectColorModeInfo : byte {
    /// <summary>
    /// Color ramp is programmable.
    /// </summary>
    ColorRampProgrammable = 0x01,

    /// <summary>
    /// Reserved bits are usable.
    /// </summary>
    ReservedBitsUsable = 0x02
}
