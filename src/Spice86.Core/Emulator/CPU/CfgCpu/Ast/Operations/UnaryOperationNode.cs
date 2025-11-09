namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

public class UnaryOperationNode(DataType dataType, UnaryOperation unaryOperation, ValueNode value) : ValueNode(dataType) {
    public UnaryOperation UnaryOperation { get; } = unaryOperation;
    public ValueNode Value { get; } = value;
    
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitUnaryOperationNode(this);
    }
}