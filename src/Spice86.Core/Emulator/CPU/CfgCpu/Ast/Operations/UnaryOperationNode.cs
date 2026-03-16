namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

public record UnaryOperationNode(DataType DataType, UnaryOperation UnaryOperation, ValueNode Value) : ValueNode(DataType) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitUnaryOperationNode(this);
    }
}