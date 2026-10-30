namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// A <c>while (Condition) { Body }</c> loop built from a natural loop whose header tests the condition before
/// the body runs. <paramref name="HeaderTarget"/> is the goto target of the loop header the condition was
/// lifted from: a <c>goto</c> to it is a <c>continue</c>, and its label prints before the loop.
/// </summary>
/// <param name="Condition">Evaluated before each iteration; the loop repeats while it is true.</param>
/// <param name="Body">The statements of one iteration.</param>
/// <param name="HeaderTarget">The goto target of the loop header.</param>
internal sealed record PreTestLoopNode(ValueNode Condition, BlockNode Body, IGotoTarget HeaderTarget) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitPreTestLoopNode(this);
}
