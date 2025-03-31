namespace Spice86.Core.Emulator.CPU.CfgCpu.AST.Operations;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.AST.Value;

public class BiOperationWithResultNode(DataType dataType, ValueNode left, Operation operation, ValueNode right) : ValueNode(dataType) {
    public ValueNode Left { get; } = left;
    public Operation Operation { get; } = operation;
    public ValueNode Right { get; } = right;
    
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitBiOperationWithResultNode(this);
    }
}