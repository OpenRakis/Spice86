namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Represents a sequence of statements that should be executed in order.
/// </summary>
public class BlockNode : IVisitableAstNode {
    /// <summary>
    /// Initializes a new instance of the BlockNode class.
    /// </summary>
    /// <param name="statements">The statements to execute in order.</param>
    public BlockNode(params IVisitableAstNode[] statements) {
        Statements = statements;
    }

    /// <summary>
    /// The statements to execute in order.
    /// </summary>
    public IReadOnlyList<IVisitableAstNode> Statements { get; }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitBlockNode(this);
    }
}
