namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Represents the read-back status byte and exposes helpers for its individual bitfields.
/// </summary>
/// <remarks>
///     <![CDATA[
/// Read Back Status Byte
/// Bit/s        Usage
/// 7            Output pin state
/// 6            Null count flags
/// 4 and 5      Access mode :
///                 0 0 = Latch count value command
///                 0 1 = Access mode: lobyte only
///                 1 0 = Access mode: hibyte only
///                 1 1 = Access mode: lobyte/hibyte
/// 1 to 3       Operating mode :
///                 0 0 0 = Mode 0 (interrupt on terminal count)
///                 0 0 1 = Mode 1 (hardware re-triggerable one-shot)
///                 0 1 0 = Mode 2 (rate generator)
///                 0 1 1 = Mode 3 (square wave generator)
///                 1 0 0 = Mode 4 (software triggered strobe)
///                 1 0 1 = Mode 5 (hardware triggered strobe)
///                 1 1 0 = Mode 2 (rate generator, same as 010b)
///                 1 1 1 = Mode 3 (square wave generator, same as 011b)
/// 0            BCD/Binary mode: 0 = 16-bit binary, 1 = four-digit BCD
/// ]]>
/// </remarks>
internal struct ReadBackStatus {
    /// <summary>
    ///     Raw status byte supplied by the control port.
    /// </summary>
    public byte Data;

    /// <summary>
    ///     Gets a value indicating whether the channel is operating in BCD mode.
    /// </summary>
    public bool BcdState => (Data & 0x01) != 0;

    /// <summary>
    ///     Gets the decoded operating mode.
    /// </summary>
    public PitMode PitMode => (PitMode)((Data >> 1) & 0x07);

    /// <summary>
    ///     Gets the access mode used for subsequent counter reads.
    /// </summary>
    public AccessMode AccessMode => (AccessMode)((Data >> 4) & 0x03);

    /// <summary>
    ///     Gets a value indicating whether no access mode bits are set.
    /// </summary>
    public bool AccessModeNone => (Data & 0x30) == 0;
}