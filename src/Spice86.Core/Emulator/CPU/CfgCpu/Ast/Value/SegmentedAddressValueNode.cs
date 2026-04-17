namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public record SegmentedAddressNode(ValueNode Segment, ValueNode Offset) : IVisitableAstNode {
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSegmentedAddressNode(this);
    }
}