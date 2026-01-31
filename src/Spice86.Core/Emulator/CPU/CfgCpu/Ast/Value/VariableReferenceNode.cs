namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// Represents a reference to a local variable in the AST.
/// Used to reference temporary variables created during instruction execution.
/// </summary>
public record VariableReferenceNode(DataType DataType, string VariableName) : ValueNode(DataType) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitVariableReferenceNode(this);
    }
}
