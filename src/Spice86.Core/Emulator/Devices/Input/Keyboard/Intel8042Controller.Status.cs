namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using System.Diagnostics;

public partial class Intel8042Controller {
    /// <summary>
    /// Encapsulates the 8042 status register for easier debugging.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    private sealed class Status {
        public Status(byte initial = 0) => FromByte(initial);

        /// <summary>
        /// bit0: output buffer status (1 = data available)
        /// </summary>
        public bool IsDataPending { get; set; }

        /// <summary>
        /// bit1: input buffer status (not used here, kept for completeness)
        /// </summary>
        public bool InputBufferFull { get; set; }

        /// <summary>
        /// bit2: system flag (not used here)
        /// </summary>
        public bool SystemFlag { get; set; }

        /// <summary>
        /// bit3: last write was command
        /// </summary>
        public bool WasLastWriteCmd { get; set; }

        /// <summary>
        /// bit4: unused/reserved in this emulation
        /// </summary>
        public bool Reserved4 { get; set; }

        /// <summary>
        /// bit5: data came from aux (mouse)
        /// </summary>
        public bool IsDataFromAux { get; set; }

        /// <summary>
        /// bit6: timeout
        /// </summary>
        public bool Timeout { get; set; }

        /// <summary>
        /// bit7: parity (not used here)
        /// </summary>
        public bool ParityError { get; set; }

        /// <summary>
        /// Converts the current status flags to their byte representation.
        /// </summary>
        /// <returns>A byte value representing the combined status flags. Each bit corresponds to a specific status flag.</returns>
        public byte ToByte() {
            byte v = 0;
            if (IsDataPending) v |= (byte)StatusBits.OutputBufferFull;
            if (InputBufferFull) v |= (byte)StatusBits.InputBufferFull;
            if (SystemFlag) v |= (byte)StatusBits.SystemFlag;
            if (WasLastWriteCmd) v |= (byte)StatusBits.LastWriteWasCommand;
            if (Reserved4) v |= (byte)StatusBits.Reserved4;
            if (IsDataFromAux) v |= (byte)StatusBits.DataFromAux;
            if (Timeout) v |= (byte)StatusBits.Timeout;
            if (ParityError) v |= (byte)StatusBits.ParityError;
            return v;
        }

        /// <summary>
        /// Parses the specified status byte and updates the corresponding status flags of the instance.
        /// </summary>
        /// <param name="value">A byte value representing the status bits to parse and apply.</param>
        public void FromByte(byte value) {
            IsDataPending = (value & (byte)StatusBits.OutputBufferFull) != 0;
            InputBufferFull = (value & (byte)StatusBits.InputBufferFull) != 0;
            SystemFlag = (value & (byte)StatusBits.SystemFlag) != 0;
            WasLastWriteCmd = (value & (byte)StatusBits.LastWriteWasCommand) != 0;
            Reserved4 = (value & (byte)StatusBits.Reserved4) != 0;
            IsDataFromAux = (value & (byte)StatusBits.DataFromAux) != 0;
            Timeout = (value & (byte)StatusBits.Timeout) != 0;
            ParityError = (value & (byte)StatusBits.ParityError) != 0;
        }

        private string DebuggerDisplay =>
            $"0x{ToByte():X2} (New={IsDataPending}, Aux={IsDataFromAux}, Cmd={WasLastWriteCmd}, TO={Timeout})";
    }
}