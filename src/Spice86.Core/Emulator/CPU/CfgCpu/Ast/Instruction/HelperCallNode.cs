namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Represents a call to a method on InstructionExecutionHelper or its properties.
/// </summary>
public class HelperCallNode : IVisitableAstNode {
    /// <summary>
    /// Name of the helper property (e.g., "Alu8", "Stack") or null for root helper methods.
    /// </summary>
    public string? HelperName { get; }
    
    /// <summary>
    /// Name of the method to call (e.g., "Add", "Push16").
    /// </summary>
    public string MethodName { get; }
    
    /// <summary>
    /// Arguments to pass to the method.
    /// </summary>
    public IReadOnlyList<IVisitableAstNode> Arguments { get; }

    public HelperCallNode(string? helperName, string methodName, IReadOnlyList<IVisitableAstNode> arguments) {
        HelperName = helperName;
        MethodName = methodName;
        Arguments = arguments;
    }

    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitHelperCallNode(this);
    }
}
