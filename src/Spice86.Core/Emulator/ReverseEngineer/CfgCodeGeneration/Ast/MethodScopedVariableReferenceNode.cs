namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A <see cref="VariableReferenceNode"/> declared at method scope: it is in scope everywhere in the
/// method, so it prints without the instruction's local suffix and a condition reading it can move out of its
/// boundary.</summary>
internal sealed record MethodScopedVariableReferenceNode(DataType DataType, string VariableName)
    : VariableReferenceNode(DataType, VariableName);
