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
internal enum AccessMode : byte {
    Latch = 0x0,
    Low = 0x1,
    High = 0x2,
    Both = 0x3
}