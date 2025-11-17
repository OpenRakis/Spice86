namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.CPU;

using System;

public partial class Intel8042Controller {
    /// <summary>
    /// Status register bit definitions.
    /// </summary>
    [Flags]
    public enum StatusBits : byte {
        /// <summary>
        /// Bit 0: Output buffer status (1 = data available).
        /// </summary>
        OutputBufferFull = 0x01,
        /// <summary>
        /// Bit 1: Input buffer status.
        /// </summary>
        InputBufferFull = 0x02,

        /// <summary>
        /// Bit 2: System flag.
        /// </summary>
        SystemFlag = 0x04,
        /// <summary>
        /// Bit 3: Last write was a command.
        /// </summary>
        LastWriteWasCommand = 0x08,
        /// <summary>
        /// Bit 4: Reserved/unused.
        /// </summary>
        Reserved4 = 0x10,
        /// <summary>
        /// Bit 5: Data came from auxiliary device (mouse).
        /// </summary>
        DataFromAux = 0x20,
        /// <summary>
        /// Bit 6: Timeout.
        /// </summary>
        Timeout = 0x40,
        /// <summary>
        /// Bit 7: Parity error.
        /// </summary>
        ParityError = 0x80,
    }
}