namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Represents a throw statement in the AST.
/// Compiles to throwing a new instance of the specified exception type with the given message.
/// </summary>
public class ThrowNode : IVisitableAstNode {
    /// <summary>
    /// Initializes a new instance of the ThrowNode class.
    /// </summary>
    /// <param name="exceptionType">The type of exception to throw. Must have a constructor accepting a single string message.</param>
    /// <param name="message">The message to pass to the exception constructor.</param>
    public ThrowNode(Type exceptionType, string message) {
        ExceptionType = exceptionType;
        Message = message;
    }

    /// <summary>
    /// The type of exception to throw.
    /// </summary>
    public Type ExceptionType { get; }

    /// <summary>
    /// The message to pass to the exception constructor.
    /// </summary>
    public string Message { get; }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitThrowNode(this);
    }
}
