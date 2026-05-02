namespace Spice86.DebuggerKnowledgeBase.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 25h: Absolute Disk Read. Reads CX logical sectors starting at logical
/// sector DX from drive AL into the buffer at DS:BX. On exit DOS leaves an extra word on
/// the stack that callers must POP. CF set on error, AX is the DOS error code.
/// </summary>
public sealed class DosInt25Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x25;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte drive = state.AL;
        ushort sectors = state.CX;
        ushort startSector = state.DX;
        ushort segment = state.DS;
        ushort offset = state.BX;
        DecodedParameter[] parameters = [
            new DecodedParameter(
                "drive",
                "AL",
                DecodedParameterKind.Register,
                drive,
                $"{(char)('A' + drive)}: (0x{drive:X2})",
                "Drive number (0=A, 1=B, ...)."),
            new DecodedParameter(
                "sector count",
                "CX",
                DecodedParameterKind.Register,
                sectors,
                $"{sectors} sector(s)",
                null),
            new DecodedParameter(
                "starting logical sector",
                "DX",
                DecodedParameterKind.Register,
                startSector,
                $"0x{startSector:X4}",
                null),
            new DecodedParameter(
                "buffer",
                "DS:BX",
                DecodedParameterKind.Register,
                ((long)segment << 16) | offset,
                $"{segment:X4}:{offset:X4}",
                "Destination buffer for the sector data.")
        ];
        return new DecodedCall(
            "DOS INT 25h",
            "Absolute Disk Read",
            "Reads CX logical sectors from drive AL starting at sector DX into DS:BX. Caller must POP the leftover flags word on return.",
            parameters,
            []);
    }
}
