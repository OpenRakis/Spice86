namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// A <c>do { Body } while (Condition);</c> loop built from a natural loop whose only back edge is a conditional jump
/// at the end of its last block.
/// </summary>
/// <param name="Body">The statements of one iteration.</param>
/// <param name="Condition">Evaluated after each iteration and after a <c>continue</c>; the loop repeats while it is true.</param>
internal sealed record DoWhileNode(BlockNode Body, ValueNode Condition) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitDoWhileNode(this);
}