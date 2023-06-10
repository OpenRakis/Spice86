namespace Spice86.Core.Emulator.Devices.Video.Registers.Enums;

/// <summary>
///     Names of the sequencer registers.
/// </summary>
public enum SequencerRegister {
    /// <summary>
    ///     Reset Register
    /// </summary>
    Reset,

    /// <summary>
    ///     Clocking Mode Register
    /// </summary>
    ClockingMode,

    /// <summary>
    ///     Map Mask Register
    /// </summary>
    PlaneMask,

    /// <summary>
    ///     Character Map Select Register
    /// </summary>
    CharacterMapSelect,

    /// <summary>
    ///     Sequencer Memory Mode Register
    /// </summary>
    MemoryMode
}