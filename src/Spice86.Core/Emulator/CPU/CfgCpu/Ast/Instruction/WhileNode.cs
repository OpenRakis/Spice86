namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// Represents a while loop in the AST.
/// Evaluates a boolean condition and executes the body block while the condition is true.
/// </summary>
public record WhileNode : IVisitableAstNode {
    /// <summary>
    /// Initializes a new instance of the WhileNode class.
    /// </summary>
    /// <param name="condition">The boolean condition to evaluate before each iteration.</param>
    /// <param name="body">The block to execute while the condition is true.</param>
    public WhileNode(ValueNode condition, BlockNode body) {
        Condition = condition;
        Body = body;
    }

    /// <summary>
    /// The boolean condition to evaluate before each iteration.
    /// </summary>
    public ValueNode Condition { get; }

    /// <summary>
    /// The block to execute while the condition is true.
    /// </summary>
    public BlockNode Body { get; }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitWhileNode(this);
    }
}