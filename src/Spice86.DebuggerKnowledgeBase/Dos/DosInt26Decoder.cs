namespace Spice86.DebuggerKnowledgeBase.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 26h: Absolute Disk Write. Writes CX logical sectors starting at logical
/// sector DX on drive AL from the buffer at DS:BX. On exit DOS leaves an extra word on
/// the stack that callers must POP. CF set on error, AX is the DOS error code.
/// </summary>
public sealed class DosInt26Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x26;
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
                "Source buffer holding the sector data to write.")
        ];
        return new DecodedCall(
            "DOS INT 26h",
            "Absolute Disk Write",
            "Writes CX logical sectors from DS:BX to drive AL starting at sector DX. Caller must POP the leftover flags word on return.",
            parameters,
            []);
    }
}
