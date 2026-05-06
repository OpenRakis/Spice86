namespace Spice86.DebuggerKnowledgeBase.Gus;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Helpers that build <see cref="DecodedParameter"/> values for GUS port decoders.
/// </summary>
internal static class GusPortParameters {
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

    public static DecodedParameter RawWord(string name, ushort port, uint value, string? note = null) {
        ushort truncated = (ushort)(value & 0xFFFF);
        return new DecodedParameter(
            name,
            $"port 0x{port:X3}",
            DecodedParameterKind.IoPort,
            truncated,
            $"0x{truncated:X4}",
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
