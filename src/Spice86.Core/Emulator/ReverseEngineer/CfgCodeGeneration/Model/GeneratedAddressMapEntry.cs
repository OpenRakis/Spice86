namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// One instruction's line range inside its generated method text, recorded while the method is
/// printed. <see cref="FirstLine"/> and <see cref="LastLine"/> are zero-based indices into the
/// method's own text; <c>LastLine &lt; FirstLine</c> marks an instruction that printed no line.
/// </summary>
internal sealed record MethodInstructionLine(SegmentedAddress Address, int FirstLine, int LastLine);

/// <summary>
/// One entry of <c>GeneratedAddressMap.json</c>: where instruction <see cref="Address"/>
/// (<c>"SSSS:OOOO"</c> hex) lives in the generated files. <see cref="FirstLine"/> and
/// <see cref="LastLine"/> are 1-based lines of <see cref="File"/>; the instruction's first line is
/// the one its ASM comment would have started at, so with comments off it is the line of the
/// instruction's first printed statement. An instruction that prints nothing is an empty range:
/// <see cref="LastLine"/> is <see cref="FirstLine"/> - 1.
/// </summary>
internal sealed record GeneratedAddressMapEntry(string Address, string File, string Method,
    int FirstLine, int LastLine);
