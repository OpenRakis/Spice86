namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// Represents an explicit type conversion (cast) in the AST.
/// </summary>
public class TypeConversionNode(DataType targetType, ValueNode value) : ValueNode(targetType) {
    public ValueNode Value { get; } = value;

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitTypeConversionNode(this);
    }
}