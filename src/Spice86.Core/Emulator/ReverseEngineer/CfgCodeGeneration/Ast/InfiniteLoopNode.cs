namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// A <c>while (true) { Body }</c> loop built from a natural loop of the CFG: an infinite loop in the decompiler sense,
/// a loop with no test in its header. Control leaves it only through a <c>break</c> that targets it, a <c>goto</c>, a
/// <c>return</c> or a <c>throw</c>.
/// </summary>
/// <param name="Body">The statements of one iteration; control returns to the first one after the last one.</param>
internal sealed record InfiniteLoopNode(BlockNode Body) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitInfiniteLoopNode(this);
}