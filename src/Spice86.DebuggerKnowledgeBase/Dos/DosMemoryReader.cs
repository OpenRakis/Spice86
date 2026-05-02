namespace Spice86.DebuggerKnowledgeBase.Dos;

using System.Text;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Helpers for the DOS decoders to read strings out of emulated memory without mutating any
/// emulator state. Kept internal to the DOS knowledge base.
/// </summary>
internal static class DosMemoryReader {
    private const int MaxStringLength = 256;

    /// <summary>
    /// Reads an ASCIIZ string at <paramref name="segment"/>:<paramref name="offset"/>, capped at
    /// <see cref="MaxStringLength"/> bytes.
    /// </summary>
    public static string ReadAsciiZ(IMemory memory, ushort segment, ushort offset) {
        return ReadTerminated(memory, segment, offset, 0x00);
    }

    /// <summary>
    /// Reads a '$'-terminated DOS string (used by INT 21h/AH=09h) at the given address.
    /// </summary>
    public static string ReadDollarTerminated(IMemory memory, ushort segment, ushort offset) {
        return ReadTerminated(memory, segment, offset, (byte)'$');
    }

    private static string ReadTerminated(IMemory memory, ushort segment, ushort offset, byte terminator) {
        uint baseAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < MaxStringLength; i++) {
            byte b = memory.UInt8[baseAddress + (uint)i];
            if (b == terminator) {
                break;
            }
            builder.Append((char)b);
        }
        return builder.ToString();
    }
}
