namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.CPU;

using System;

public partial class Intel8042Controller {
    /// <summary>
    /// Line parameter masks used by pulse-lines commands (0xF0-0xFF).
    /// </summary>
    [Flags]
    public enum LineParam : byte {
        /// <summary>
        /// Reset line mask (bit 0).
        /// </summary>
        Reset = 0b0001,
        /// <summary>
        /// All lines mask (bits 0..3 set).
        /// </summary>
        AllLines = 0b1111,
        /// <summary>
        /// All lines except Reset (bits 1..3 set).
        /// </summary>
        AllLinesExceptReset = 0b1110,
    }
}