namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>One <c>case</c> of a <see cref="SwitchNode"/>; the label prints as <c>0x{Label:X4}</c>.</summary>
/// <param name="Label">The case value the section matches.</param>
/// <param name="Body">The statements run when the section matches.</param>
internal sealed record SwitchSection(int Label, BlockNode Body);
