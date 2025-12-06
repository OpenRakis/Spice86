namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

public partial class PS2Keyboard {
    /// <summary>
    /// Represents code information specific to Set3, including flags for typematic, make, and break code enablement.
    /// </summary>
    private class Set3CodeInfoEntry {
        /// <summary>
        /// Indicates whether typematic behavior is enabled.
        /// </summary>
        public bool IsEnabledTypematic = true;

        /// <summary>
        /// Indicates whether the make operation is enabled.
        /// </summary>
        public bool IsEnabledMake = true;

        /// <summary>
        /// Indicates whether the break functionality is enabled.
        /// </summary>
        public bool IsEnabledBreak = true;
    }
}