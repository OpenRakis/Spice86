namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

public record BinaryOperationNode(DataType DataType, ValueNode Left, BinaryOperation BinaryOperation, ValueNode Right) : ValueNode(DataType) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitBinaryOperationNode(this);
    }
}