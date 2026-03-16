namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// Represents an explicit type conversion (cast) in the AST.
/// </summary>
public record TypeConversionNode(DataType DataType, ValueNode Value) : ValueNode(DataType) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitTypeConversionNode(this);
    }
}
