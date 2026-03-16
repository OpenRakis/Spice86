namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.CPU;

using System;

public partial class Intel8042Controller {
    /// <summary>
    /// Output port (P2) bit definitions.
    /// </summary>
    [Flags]
    public enum OutputPortBits : byte {
        /// <summary>
        /// Bit 0: 1 = normal, 0 = CPU reset asserted.
        /// </summary>
        ResetNotAsserted = 1 << 0,
        /// <summary>
        /// Bit 1: A20 line enabled.
        /// </summary>
        A20Enabled = 1 << 1,
        /// <summary>
        /// Bit 4: Keyboard IRQ1 active.
        /// </summary>
        KeyboardIrqActive = 1 << 4,
        /// <summary>
        /// Bit 5: Mouse IRQ12 active.
        /// </summary>
        MouseIrqActive = 1 << 5,
    }
}