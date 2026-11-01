namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Shared.Emulator.Memory;

/// <summary>An <c>AluN</c> operation value with its status-flag effect recorded once.</summary>
/// <param name="Call">The wrapped <c>AluN</c> call.</param>
/// <param name="Flags">The flag effect of the operation.</param>
internal sealed record AluOperationNode(AluCallValueNode Call, FlagEffect Flags) : ValueNode(Call.DataType) {
    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitAluOperationNode(this);
}
