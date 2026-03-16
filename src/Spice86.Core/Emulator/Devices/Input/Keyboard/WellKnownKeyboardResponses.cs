namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

public partial class PS2Keyboard {
    /// <summary>
    /// Defines well-known PS/2 keyboard response/data bytes sent to the controller (port 0x60).
    /// </summary>
    /// <remarks>
    /// Reference:
    /// - https://www.win.tue.nl/~aeb/linux/kbd/scancodes-11.html
    /// </remarks>
    public enum WellKnownKeyboardResponses : byte {
        /// <summary>
        /// Acknowledge byte sent after most valid commands.
        /// </summary>
        Ack = 0xFA,

        /// <summary>
        /// Request to resend the last command/byte (typically after an error).
        /// </summary>
        Resend = 0xFE,

        /// <summary>
        /// Response to the ECHO command. Note: this is sent without a preceding ACK.
        /// </summary>
        Echo = 0xEE,

        /// <summary>
        /// Basic Assurance Test (BAT) passed.
        /// </summary>
        BatOk = 0xAA,

        /// <summary>
        /// First byte (prefix) of the two-byte keyboard identification sequence returned by the Identify command.
        /// </summary>
        IdentifyPrefix = 0xAB,

        /// <summary>
        /// Second byte of the Identify response for the common MF2 101/102-key keyboard (sequence: 0xAB, 0x83).
        /// </summary>
        IdentifyMf2 = 0x83,

        /// <summary>
        /// Second byte of the Identify response for many space saver keyboards (sequence: 0xAB, 0x84).
        /// </summary>
        IdentifySpaceSaver = 0x84,

        /// <summary>
        /// Second byte of the Identify response for many 122-key keyboards (sequence: 0xAB, 0x86).
        /// </summary>
        Identify122Key = 0x86
    }
}