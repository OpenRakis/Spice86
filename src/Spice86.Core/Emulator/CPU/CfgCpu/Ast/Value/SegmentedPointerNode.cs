namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public record SegmentedPointerNode(DataType DataType, ValueNode Segment, ValueNode? DefaultSegment, ValueNode Offset) : ValueNode(DataType) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSegmentedPointer(this);
    }
}