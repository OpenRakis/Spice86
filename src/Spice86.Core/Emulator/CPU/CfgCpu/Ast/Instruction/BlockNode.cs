namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Represents a sequence of statements to be executed in order.
/// </summary>
public class BlockNode : IVisitableAstNode {
    /// <summary>
    /// The statements to execute in sequence.
    /// </summary>
    public IReadOnlyList<IVisitableAstNode> Statements { get; }

    public BlockNode(IReadOnlyList<IVisitableAstNode> statements) {
        Statements = statements;
    }

    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitBlockNode(this);
    }
}
