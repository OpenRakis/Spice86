namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

/// <summary>Helper for extracting the octal-encoded fields (hi2, mid3, lo3) from an x86 opcode or ModRM byte.</summary>
internal static class X86OctalParser {
    /// <summary>Extracts bits 2-0 of a byte (the low 3 bits in octal encoding).</summary>
    internal static int Lo3(int value) => value & 0b111;

    /// <summary>Extracts bits 5-3 of a byte (the middle 3 bits in octal encoding).</summary>
    internal static int Mid3(int value) => (value >> 3) & 0b111;

    /// <summary>Extracts bits 7-6 of a byte (the high 2 bits in octal encoding).</summary>
    internal static uint Hi2(int value) => (uint)((value >> 6) & 0b11);
}
