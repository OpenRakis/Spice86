namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Represents a throw statement in the AST.
/// Compiles to throwing a new instance of the specified exception type with the given message.
/// </summary>
public record ThrowNode(Type ExceptionType, string Message) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitThrowNode(this);
    }
}
