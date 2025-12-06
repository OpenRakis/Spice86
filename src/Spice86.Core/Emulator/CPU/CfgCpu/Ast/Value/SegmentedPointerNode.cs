namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public class SegmentedPointerNode(DataType dataType, ValueNode segment, ValueNode offset) : ValueNode(dataType) {
    public ValueNode Segment { get; } = segment;
    public ValueNode Offset { get; } = offset;

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSegmentedPointer(this);
    }
}