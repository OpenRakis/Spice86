namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Encodes the read/write sequencing bits used by the data port state machine.
/// </summary>
/// <remarks>
///     <para>Bits 4 and 5 of the control word map as follows:</para>
///     <para>00 = latch count value command</para>
///     <para>01 = low byte only</para>
///     <para>10 = high byte only</para>
///     <para>11 = low byte followed by high byte</para>
/// </remarks>
public enum AccessMode : byte {
    /// <summary>
    ///     Counter latch command in a control word. As the position of a channel's read or write state machine, the
    ///     low byte of a low-then-high pair was transferred and the high byte comes next.
    /// </summary>
    Latch = 0x0,

    /// <summary>Only the low byte is read or written.</summary>
    Low = 0x1,

    /// <summary>Only the high byte is read or written.</summary>
    High = 0x2,

    /// <summary>
    ///     The low byte is read or written, then the high byte. As the position of a state machine, the low byte
    ///     comes next.
    /// </summary>
    Both = 0x3
}
