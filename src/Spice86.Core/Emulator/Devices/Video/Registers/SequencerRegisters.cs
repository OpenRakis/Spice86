namespace Spice86.Core.Emulator.Devices.Video.Registers;

using Spice86.Core.Emulator.Devices.Video.Registers.Enums;
using Spice86.Core.Emulator.Devices.Video.Registers.Sequencer;

/// <summary>
///     Emulates the VGA Sequencer registers.
/// </summary>
public class SequencerRegisters {
    /// <summary>
    ///     The Sequencer Address field (bits 2− 0) contains the index value that points to the data register to be
    ///     accessed.
    /// </summary>
    public SequencerRegister Address { get; set; }

    /// <summary>
    ///     Gets the Reset register.
    /// </summary>
    public ResetRegister ResetRegister { get; } = new();

    /// <summary>
    ///     Gets the Clocking Mode register.
    /// </summary>
    public ClockingModeRegister ClockingModeRegister { get; } = new();

    /// <summary>
    ///     Gets the Map Mask register.
    /// </summary>
    public Register8 PlaneMaskRegister { get; } = new();

    /// <summary>
    ///     Gets the Character Map Select register.
    /// </summary>
    public CharacterMapSelectRegister CharacterMapSelectRegister { get; } = new();

    /// <summary>
    ///     Gets the Sequencer Memory Mode register.
    /// </summary>
    public MemoryModeRegister MemoryModeRegister { get; } = new();

    /// <summary>
    ///     Returns the current value of a sequencer register.
    /// </summary>
    /// <returns>Current value of the indicated register.</returns>
    public byte ReadRegister(SequencerRegister address) {
        return address switch {
            SequencerRegister.Reset => ResetRegister.Value,
            SequencerRegister.ClockingMode => ClockingModeRegister.Value,
            SequencerRegister.PlaneMask => PlaneMaskRegister.Value,
            SequencerRegister.CharacterMapSelect => CharacterMapSelectRegister.Value,
            SequencerRegister.MemoryMode => MemoryModeRegister.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(SequencerRegister), address, "Unknown sequencer register")
        };
    }

    /// <summary>
    ///     Returns the current value of the sequencer register indicated by the Address field.
    /// </summary>
    public byte ReadRegister() {
        return ReadRegister(Address);
    }

    /// <summary>
    ///     Writes to a sequencer register.
    /// </summary>
    /// <param name="address">Which of the sequencer registers to write to</param>
    /// <param name="value">Value to write to register.</param>
    public void WriteRegister(SequencerRegister address, byte value) {
        switch (address) {
            case SequencerRegister.Reset:
                ResetRegister.Value = value;
                break;
            case SequencerRegister.ClockingMode:
                ClockingModeRegister.Value = value;
                break;
            case SequencerRegister.PlaneMask:
                PlaneMaskRegister.Value = value;
                break;
            case SequencerRegister.CharacterMapSelect:
                CharacterMapSelectRegister.Value = value;
                break;
            case SequencerRegister.MemoryMode:
                MemoryModeRegister.Value = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(SequencerRegister), address, "Unknown sequencer register");
        }
    }

    /// <summary>
    ///     Writes to the sequencer register indicated by the Address field.
    /// </summary>
    /// <param name="value">The byte value being written.</param>
    public void WriteRegister(byte value) {
        WriteRegister(Address, value);
    }
}