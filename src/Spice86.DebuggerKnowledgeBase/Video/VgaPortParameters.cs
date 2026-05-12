namespace Spice86.DebuggerKnowledgeBase.Video;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Helpers that build <see cref="DecodedParameter"/> values for VGA port decoders.
/// </summary>
internal static class VgaPortParameters {
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

    public static DecodedParameter IndexWithName(string name, ushort port, byte index, string registerName, string? note = null) {
        return new DecodedParameter(
            name,
            $"port 0x{port:X3}",
            DecodedParameterKind.IoPort,
            index,
            $"0x{index:X2} ({registerName})",
            note);
    }
}
