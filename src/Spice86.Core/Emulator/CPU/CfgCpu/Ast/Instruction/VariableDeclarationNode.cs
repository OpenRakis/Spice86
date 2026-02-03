namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// Represents a local variable declaration with initialization in the AST.
/// Used to create temporary variables during instruction execution.
/// Example: "ushort result = Alu8.Mul(AL, RM8);"
/// </summary>
public class VariableDeclarationNode(DataType dataType, string variableName, ValueNode initializer) : IVisitableAstNode {
    /// <summary>
    /// The data type of the variable.
    /// </summary>
    public DataType DataType { get; } = dataType;

    /// <summary>
    /// The name of the variable being declared.
    /// </summary>
    public string VariableName { get; } = variableName;

    /// <summary>
    /// The expression that initializes the variable.
    /// </summary>
    public ValueNode Initializer { get; } = initializer;

    /// <summary>
    /// A reference to this declared variable.
    /// Created once since every declaration will be referenced at least once.
    /// </summary>
    public VariableReferenceNode Reference { get; } = new(dataType, variableName);

    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitVariableDeclarationNode(this);
    }
}
