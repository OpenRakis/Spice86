namespace Spice86.DebuggerKnowledgeBase.Mpu401;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Helpers that build <see cref="DecodedParameter"/> values for MPU-401 port decoders.
/// </summary>
internal static class Mpu401PortParameters {
    public static DecodedParameter RawByte(string name, ushort port, uint value, string? note = null) {
        byte truncated = (byte)(value & 0xFF);
        return new DecodedParameter(
            name,
            $"port 0x{port:X3}",
            DecodedParameterKind.IoPort,
            truncated,
            $"0x{truncated:X2}",
            note);
    }

    public static DecodedParameter ByteWithName(string name, ushort port, byte value, string mnemonic, string? note = null) {
        return new DecodedParameter(
            name,
            $"port 0x{port:X3}",
            DecodedParameterKind.IoPort,
            value,
            $"0x{value:X2} ({mnemonic})",
            note);
    }
}
