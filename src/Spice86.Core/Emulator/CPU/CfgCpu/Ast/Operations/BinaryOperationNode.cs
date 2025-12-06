namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

public class BinaryOperationNode(DataType dataType, ValueNode left, BinaryOperation binaryOperation, ValueNode right) : ValueNode(dataType) {
    public ValueNode Left { get; } = left;
    public BinaryOperation BinaryOperation { get; } = binaryOperation;
    public ValueNode Right { get; } = right;

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitBinaryOperationNode(this);
    }
}