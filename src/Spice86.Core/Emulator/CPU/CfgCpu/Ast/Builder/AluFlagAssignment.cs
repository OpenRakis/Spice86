namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

/// <summary>The assignment of one status flag in an <see cref="AluExpansion"/>.</summary>
/// <param name="Flag">The assigned flag (exactly one bit).</param>
/// <param name="Value">The boolean value assigned to the flag.</param>
internal sealed record AluFlagAssignment(StatusFlags Flag, ValueNode Value) {
    /// <summary>The statement <c>Flag = Value</c>.</summary>
    public BinaryOperationNode ToAssignment() =>
        new(DataType.BOOL, new CpuFlagNode((ushort)Flag), BinaryOperation.ASSIGN, Value);
}
