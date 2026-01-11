namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Represents a void method call on InstructionExecutionHelper or its properties.
/// Used for side-effects only (e.g., MoveIpAndSetNextNode, Push16).
/// Cannot be used as an expression operand.
/// For methods that return values, use <see cref="MethodCallValueNode"/> instead.
/// </summary>
public class MethodCallNode : IVisitableAstNode {
    /// <summary>
    /// Initializes a new instance of the MethodCallNode class.
    /// </summary>
    /// <param name="propertyPath">The property path to access the target object (e.g., "Alu8", "Stack") or null for root helper.</param>
    /// <param name="methodName">The name of the method to call (e.g., "MoveIpAndSetNextNode", "Push16").</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public MethodCallNode(string? propertyPath, string methodName, params IVisitableAstNode[] arguments) {
        PropertyPath = propertyPath;
        MethodName = methodName;
        Arguments = arguments;
    }

    /// <summary>
    /// The property path to access the target object (e.g., "Alu8", "Stack").
    /// If null, the method is called directly on the root helper (InstructionExecutionHelper).
    /// </summary>
    public string? PropertyPath { get; }

    /// <summary>
    /// The name of the method to call (e.g., "MoveIpAndSetNextNode", "Push16").
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// The arguments to pass to the method.
    /// </summary>
    public IReadOnlyList<IVisitableAstNode> Arguments { get; }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitMethodCallNode(this);
    }
}
