namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;

public class PointerAstBuilder {
    public AbsolutePointerNode ToAbsolutePointer(DataType targetDataType, uint address) {
        return new AbsolutePointerNode(targetDataType, new ConstantNode(DataType.UINT32, address));
    }
    
    public ValueNode ToSegmentedPointer(DataType targetDataType, SegmentRegisterIndex segmentRegisterIndex, ValueNode offset) {
        return ToSegmentedPointer(targetDataType, (int)segmentRegisterIndex, null, offset);
    }

    public ValueNode ToSegmentedPointer(DataType targetDataType, int segmentRegisterIndex, int? defaultSegmentRegisterIndex, ValueNode offset) {
        SegmentRegisterNode segment = new(segmentRegisterIndex);
        SegmentRegisterNode? defaultSegment =
            defaultSegmentRegisterIndex == null ? null : new(defaultSegmentRegisterIndex.Value);
        return ToSegmentedPointer(targetDataType, segment, defaultSegment, offset);
    }
    
    public ValueNode ToSegmentedPointer(DataType targetDataType, ValueNode segment, ValueNode? defaultSegment, ValueNode offset) {
        return new SegmentedPointerNode(targetDataType, segment, defaultSegment, offset);
    }

    /// <summary>
    /// Creates a new segmented pointer with an adjusted offset.
    /// Takes an existing segmented pointer and adds an offset adjustment to it.
    /// </summary>
    public ValueNode WithOffsetAdjustment(SegmentedPointerNode pointer, ValueNode offsetAdjustment) {
        DataType offsetDataType = pointer.Offset.DataType;
        ValueNode adjustedOffset = new BinaryOperationNode(offsetDataType, pointer.Offset, BinaryOperation.PLUS, offsetAdjustment);
        return new SegmentedPointerNode(pointer.DataType, pointer.Segment, pointer.DefaultSegment, adjustedOffset);
    }
}