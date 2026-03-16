namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// Represents a value-returning method call on InstructionExecutionHelper or its properties.
/// Extends <see cref="ValueNode"/> to enable use as operands in expressions
/// (e.g., in BinaryOperationNode, assignments, etc.).
/// For void methods, use <see cref="MethodCallNode"/> instead.
/// </summary>
public record MethodCallValueNode : ValueNode {
    /// <summary>
    /// Initializes a new instance of the MethodCallValueNode class.
    /// </summary>
    /// <param name="dataType">The data type of the returned value.</param>
    /// <param name="propertyPath">The property path to access the target object (e.g., "Alu8", "Stack") or null for root helper.</param>
    /// <param name="methodName">The name of the method to call (e.g., "Add", "Push16").</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public MethodCallValueNode(DataType dataType, string? propertyPath, string methodName, params IVisitableAstNode[] arguments)
        : base(dataType) {
        CallNode = new MethodCallNode(propertyPath, methodName, arguments);
    }

    /// <summary>
    /// The underlying method call information.
    /// Contains the property path, method name, and arguments.
    /// </summary>
    public MethodCallNode CallNode { get; }

    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitMethodCallValueNode(this);
    }
}
