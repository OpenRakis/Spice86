namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// A loop that tests <see cref="Condition"/> before each iteration and <see cref="ExitCondition"/> after it:
/// <c>while (Condition) { Body; if (ExitCondition) break; }</c>.
/// </summary>
/// <param name="Condition">The condition tested before each iteration.</param>
/// <param name="Body">The statements of one iteration.</param>
/// <param name="ExitCondition">The condition tested after each iteration; the loop ends when it is true.</param>
public record WhileWithExitNode(ValueNode Condition, BlockNode Body, ValueNode ExitCondition) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => astVisitor.VisitWhileWithExitNode(this);
}
