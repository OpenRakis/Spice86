namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Shared.Emulator.Memory;

/// <summary>An <c>AluN</c> statement such as <c>UpdateFlags</c> with its status-flag effect recorded once.</summary>
/// <param name="Call">The wrapped <c>AluN</c> call.</param>
/// <param name="Flags">The flag effect of the statement.</param>
internal sealed record AluStatementNode(AluCallNode Call, FlagEffect Flags) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitAluStatementNode(this);
}
