namespace Spice86.Aeon.Emulator.Video {
    using Spice86.Aeon.Emulator.Video.Registers.Sequencer;

    /// <summary>
    /// Emulates the VGA Sequencer registers.
    /// </summary>
    public class SequencerRegisters {

        /// <summary>
        /// The Sequencer Address field (bits 2− 0) contains the index value that points to the data register to be
        /// accessed.
        /// </summary>
        public SequencerRegister SequencerAddress { get; set; }

        /// <summary>
        /// Gets the Reset register.
        /// </summary>
        public ResetRegister ResetRegister { get; } = new();

        /// <summary>
        /// Gets the Clocking Mode register.
        /// </summary>
        public ClockingModeRegister ClockingModeRegister { get; } = new();

        /// <summary>
        /// Gets the Map Mask register.
        /// </summary>
        public PlaneMaskRegister PlaneMaskRegister { get; } = new();

        /// <summary>
        /// Gets the Character Map Select register.
        /// </summary>
        public CharacterMapSelectRegister CharacterMapSelectRegister { get; } = new();

        /// <summary>
        /// Gets the Sequencer Memory Mode register.
        /// </summary>
        public MemoryModeRegister MemoryModeRegister { get; } = new();

        /// <summary>
        /// Returns the current value of a sequencer register.
        /// </summary>
        /// <returns>Current value of the register.</returns>
        public byte ReadRegister() {
            return SequencerAddress switch {
                SequencerRegister.Reset => ResetRegister.Value,
                SequencerRegister.ClockingMode => ClockingModeRegister.Value,
                SequencerRegister.PlaneMask => PlaneMaskRegister.Value,
                SequencerRegister.CharacterMapSelect => CharacterMapSelectRegister.Value,
                SequencerRegister.SequencerMemoryMode => MemoryModeRegister.Value,
                _ => 0
            };
        }

        /// <summary>
        /// Writes to a sequencer register.
        /// </summary>
        /// <param name="value">Value to write to register.</param>
        public void WriteRegister(byte value) {
            switch (SequencerAddress) {
                case SequencerRegister.Reset:
                    ResetRegister.Value = value;
                    break;
                case SequencerRegister.ClockingMode:
                    ClockingModeRegister.Value = value;
                    break;
                case SequencerRegister.PlaneMask:
                    PlaneMaskRegister.MaskValue = value;
                    break;
                case SequencerRegister.CharacterMapSelect:
                    CharacterMapSelectRegister.Value = value;
                    break;
                case SequencerRegister.SequencerMemoryMode:
                    MemoryModeRegister.Value = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(SequencerAddress), SequencerAddress, "Unknown sequencer register");
            }
        }

        public string Explain(byte value) => SequencerAddress.Explain(value);

    }

}