namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Represents a sequence of statements that should be executed in order.
/// Can include variable declarations (VariableDeclarationNode) which will be scoped to this block.
/// </summary>
public class BlockNode : IVisitableAstNode {
    /// <summary>
    /// Initializes a new instance of the BlockNode class.
    /// </summary>
    /// <param name="statements">The statements to execute in order. Can include VariableDeclarationNode instances.</param>
    public BlockNode(params IVisitableAstNode[] statements) {
        Statements = statements;
    }

    /// <summary>
    /// The statements to execute in order.
    /// May include VariableDeclarationNode instances which are scoped to this block.
    /// </summary>
    public IReadOnlyList<IVisitableAstNode> Statements { get; }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitBlockNode(this);
    }
}
