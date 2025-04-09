namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;

public class RegisterAstBuilder {
    public ValueNode Reg8(RegisterIndex registerIndex) {
        return Reg(DataType.UINT8, registerIndex);
    }

    public ValueNode Reg16(RegisterIndex registerIndex) {
        return Reg(DataType.UINT16, registerIndex);
    }
    
    public ValueNode SReg(SegmentRegisterIndex registerIndex) {
        return SReg((int)registerIndex);
    }
    
    public ValueNode SReg(int segmentRegisterIndex) {
        return new SegmentRegisterNode(segmentRegisterIndex);
    }

    public ValueNode Reg32(RegisterIndex registerIndex) {
        return Reg(DataType.UINT32, registerIndex);
    }

    public ValueNode Accumulator(DataType dataType) {
        return new RegisterNode(dataType, (int)RegisterIndex.AxIndex);
    }

    public ValueNode Reg(DataType dataType, RegisterIndex registerIndex) {
        return Reg(dataType, (int)registerIndex);
    }

    public ValueNode Reg(DataType dataType, int registerIndex) {
        return new RegisterNode(dataType, registerIndex);
    }
}