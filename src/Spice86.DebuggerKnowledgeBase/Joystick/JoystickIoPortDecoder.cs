namespace Spice86.DebuggerKnowledgeBase.Joystick;

using System.Collections.Generic;
using System.Text;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes PC/AT joystick gameport I/O reads and writes at the standard 0x201 port
/// (and the 0x200..0x207 gameport address window aliases). Mirrors dosbox-staging's
/// <c>joystick.cpp</c>: the gameport is a single 8-bit register that combines the
/// four button lines (low nibble inverted, bits 4..7) with the four resistor-capacitor
/// timer outputs for the X1/Y1/X2/Y2 axes (bits 0..3); writing any value to the port
/// fires the one-shot timers used to measure each axis's resistance.
/// </summary>
/// <remarks>
/// <para>
/// The IBM PC Game Control Adapter and every clone (CH Products, Gravis, Sound Blaster
/// gameport, etc.) share the same one-byte protocol:
/// </para>
/// <list type="bullet">
///   <item><description>
/// On <c>OUT 0x201, *</c> the 558 quad-timer chip is triggered: each of the four
/// axis bits (0..3) goes high and stays high for a duration proportional to the
/// position of the corresponding potentiometer (R * 0.01uF). Software polls the
/// port in a tight loop and counts iterations until each bit returns to zero.
///   </description></item>
///   <item><description>
/// On <c>IN 0x201</c> the byte returned has format
/// <c>BBBB AAAA</c> where the high nibble (BBBB) gives the four button states
/// (bits 4..7 = Joy-1 button A, Joy-1 button B, Joy-2 button A, Joy-2 button B;
/// bit clear = pressed) and the low nibble (AAAA) gives the four axis timer
/// outputs (bit 0 = Joy-1 X, bit 1 = Joy-1 Y, bit 2 = Joy-2 X, bit 3 = Joy-2 Y).
///   </description></item>
/// </list>
/// <para>
/// Spice86 currently emulates an unplugged joystick (see
/// <c>Spice86.Core.Emulator.Devices.Input.Joystick.Joystick</c> at port 0x201)
/// but applications still poll the port. Decoding those probes makes them legible
/// in the debugger UI. The decoder is pure / read-only.
/// </para>
/// </remarks>
public sealed class JoystickIoPortDecoder : IIoPortDecoder {
    private const string Subsystem = "Joystick Gameport I/O Ports";

    /// <summary>
    /// Standard PC/AT joystick gameport address. Hardware decodes the bottom three
    /// address lines partially, so accesses to the whole 0x200..0x207 window also
    /// reach the gameport on most cards. The canonical port is <c>0x201</c>.
    /// </summary>
    public const ushort GamePortBase = 0x200;

    /// <summary>
    /// Last port (inclusive) of the gameport decode window.
    /// </summary>
    public const ushort GamePortEnd = 0x207;

    /// <inheritdoc />
    public bool CanDecode(ushort port) {
        return port >= GamePortBase && port <= GamePortEnd;
    }

    /// <inheritdoc />
    public DecodedCall DecodeRead(ushort port, uint value, int width) {
        return DecodeReadInternal(port, value, width);
    }

    /// <inheritdoc />
    public DecodedCall DecodeWrite(ushort port, uint value, int width) {
        return DecodeWriteInternal(port, value, width);
    }

    private static DecodedCall DecodeReadInternal(ushort port, uint value, int width) {
        byte raw = (byte)(value & 0xFF);
        string mnemonic = FormatStatusByte(raw);
        IReadOnlyList<DecodedParameter> parameters = [
            JoystickPortParameters.ByteWithName("status", port, raw, mnemonic,
                "high nibble = button states (0 = pressed); low nibble = axis RC timer outputs (high while charging)")
        ];
        return Build(
            $"Gameport Read Status (0x{port:X3})",
            "Returns the 8-bit gameport status: bits 4..7 = Joy-1 BtnA / Joy-1 BtnB / Joy-2 BtnA / Joy-2 BtnB (clear = pressed); bits 0..3 = Joy-1 X / Joy-1 Y / Joy-2 X / Joy-2 Y axis timers (set while the 558 one-shot is still timing the potentiometer).",
            port, value, width, IoPortAccessDirection.Read,
            parameters);
    }

    private static DecodedCall DecodeWriteInternal(ushort port, uint value, int width) {
        IReadOnlyList<DecodedParameter> parameters = [
            JoystickPortParameters.RawByte("trigger", port, value,
                "value is ignored; any write triggers the 558 one-shot to begin timing all four axes")
        ];
        return Build(
            $"Gameport Trigger Axis Timers (0x{port:X3})",
            "Fires the 558 quad one-shot: every axis timer bit (bits 0..3) goes high for a duration proportional to the corresponding potentiometer's resistance. Software then polls the port until each bit clears, counting iterations to derive the X1/Y1/X2/Y2 axis values.",
            port, value, width, IoPortAccessDirection.Write,
            parameters);
    }

    private static string FormatStatusByte(byte raw) {
        StringBuilder sb = new StringBuilder();
        // Buttons: bit clear = pressed.
        AppendButton(sb, raw, 4, "J1A");
        AppendButton(sb, raw, 5, "J1B");
        AppendButton(sb, raw, 6, "J2A");
        AppendButton(sb, raw, 7, "J2B");
        // Axes: bit set = timer still charging.
        AppendAxis(sb, raw, 0, "J1X");
        AppendAxis(sb, raw, 1, "J1Y");
        AppendAxis(sb, raw, 2, "J2X");
        AppendAxis(sb, raw, 3, "J2Y");
        if (sb.Length == 0) {
            return "all buttons released, all axis timers settled";
        }
        return sb.ToString();
    }

    private static void AppendButton(StringBuilder sb, byte raw, int bit, string name) {
        bool pressed = (raw & (1 << bit)) == 0;
        if (pressed) {
            if (sb.Length > 0) {
                sb.Append(", ");
            }
            sb.Append(name).Append(" pressed");
        }
    }

    private static void AppendAxis(StringBuilder sb, byte raw, int bit, string name) {
        bool charging = (raw & (1 << bit)) != 0;
        if (charging) {
            if (sb.Length > 0) {
                sb.Append(", ");
            }
            sb.Append(name).Append(" timing");
        }
    }

    private static DecodedCall Build(
        string functionName,
        string shortDescription,
        ushort port,
        uint value,
        int width,
        IoPortAccessDirection direction,
        IReadOnlyList<DecodedParameter> parameters) {
        string verb;
        if (direction == IoPortAccessDirection.Read) {
            verb = "in";
        } else {
            verb = "out";
        }
        string desc = $"{verb} 0x{port:X3} (width={width}): {shortDescription}";
        _ = value;
        return new DecodedCall(Subsystem, functionName, desc, parameters, []);
    }
}
