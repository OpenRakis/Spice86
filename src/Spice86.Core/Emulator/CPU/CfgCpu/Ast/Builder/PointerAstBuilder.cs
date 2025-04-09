namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;

public class PointerAstBuilder {
    public AbsolutePointerNode ToAbsolutePointer(DataType targetDataType, uint address) {
        return new AbsolutePointerNode(targetDataType, new ConstantNode(DataType.UINT32, address));
    }
    
    public ValueNode ToSegmentedPointer(DataType targetDataType, SegmentRegisterIndex segmentRegisterIndex, ValueNode offset) {
        return ToSegmentedPointer(targetDataType, (int)segmentRegisterIndex, offset);
    }

    public ValueNode ToSegmentedPointer(DataType targetDataType, int segmentRegisterIndex, ValueNode offset) {
        return ToSegmentedPointer(targetDataType, new SegmentRegisterNode(segmentRegisterIndex), offset);
    }

    public ValueNode ToSegmentedPointer(DataType targetDataType, ValueNode segment, ValueNode offset) {
        return new SegmentedPointer(targetDataType, segment, offset);
    }
}