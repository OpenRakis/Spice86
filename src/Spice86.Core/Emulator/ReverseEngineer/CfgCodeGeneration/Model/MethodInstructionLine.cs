namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// One instruction's line range inside its generated method text, recorded while the method is
/// printed. <see cref="FirstLine"/> and <see cref="LastLine"/> are zero-based indices into the
/// method's own text; <c>LastLine &lt; FirstLine</c> marks an instruction that printed no line.
/// </summary>
internal sealed record MethodInstructionLine(SegmentedAddress Address, int FirstLine, int LastLine);
