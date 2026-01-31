namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// Represents a reference to a local variable in the AST.
/// Used to reference temporary variables created during instruction execution.
/// </summary>
public class VariableReferenceNode(DataType dataType, string variableName) : ValueNode(dataType) {
    /// <summary>
    /// The name of the variable being referenced.
    /// </summary>
    public string VariableName { get; } = variableName;

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitVariableReferenceNode(this);
    }
}
