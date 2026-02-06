namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// Represents a conditional if/else statement in the AST.
/// Evaluates a boolean condition and executes either the true case or false case block.
/// </summary>
public record IfElseNode : IVisitableAstNode {
    /// <summary>
    /// Initializes a new instance of the IfElseNode class.
    /// </summary>
    /// <param name="condition">The boolean condition to evaluate.</param>
    /// <param name="trueCase">The node to execute if the condition is true.</param>
    /// <param name="falseCase">The node to execute if the condition is false.</param>
    public IfElseNode(ValueNode condition, IVisitableAstNode trueCase, IVisitableAstNode falseCase) {
        Condition = condition;
        TrueCase = trueCase;
        FalseCase = falseCase;
    }

    /// <summary>
    /// The boolean condition to evaluate.
    /// </summary>
    public ValueNode Condition { get; }

    /// <summary>
    /// The node to execute if the condition is true.
    /// </summary>
    public IVisitableAstNode TrueCase { get; }

    /// <summary>
    /// The node to execute if the condition is false.
    /// </summary>
    public IVisitableAstNode FalseCase { get; }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitIfElseNode(this);
    }
}
