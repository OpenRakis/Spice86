namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// Represents an assignment statement where the target is a ValueNode and the value is any AST node (including helper calls).
/// </summary>
public class AssignmentNode : IVisitableAstNode {
    /// <summary>
    /// The target of the assignment (must be an lvalue like a register or memory location).
    /// </summary>
    public ValueNode Target { get; }
    
    /// <summary>
    /// The value to assign (can be any AST node).
    /// </summary>
    public IVisitableAstNode Value { get; }

    public AssignmentNode(ValueNode target, IVisitableAstNode value) {
        Target = target;
        Value = value;
    }

    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitAssignmentNode(this);
    }
}
