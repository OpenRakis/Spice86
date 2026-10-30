namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>One <c>case</c> of a <see cref="SwitchNode"/>; the label prints as <c>0x{Label:X4}</c>, or
/// <c>0xSSSS_OOOO</c> when the switch value is a <see cref="DataType.UINT32"/> segmented address.</summary>
/// <param name="Label">The case value the section matches.</param>
/// <param name="Body">The statements run when the section matches.</param>
internal sealed record SwitchSection(uint Label, BlockNode Body);
