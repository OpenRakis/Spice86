namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;

public class RegisterAstBuilder {
    private readonly TypeConversionAstBuilder _typeConversion = new();

    public ValueNode Reg8L(RegisterIndex registerIndex) {
        return Reg(DataType.UINT8, registerIndex);
    }

    public ValueNode Reg8H(RegisterIndex registerIndex) {
        return Reg(DataType.UINT8, (int)registerIndex + 4);
    }

    public ValueNode Reg8LSigned(RegisterIndex registerIndex) {
        return _typeConversion.ToSigned(Reg8L(registerIndex));
    }

    public ValueNode Reg8HSigned(RegisterIndex registerIndex) {
        return _typeConversion.ToSigned(Reg8H(registerIndex));
    }

    public ValueNode Reg8(RegisterIndex registerIndex) {
        return Reg8L(registerIndex);
    }

    public ValueNode Reg8Signed(RegisterIndex registerIndex) {
        return Reg8LSigned(registerIndex);
    }

    public ValueNode Reg16(RegisterIndex registerIndex) {
        return Reg(DataType.UINT16, registerIndex);
    }

    public ValueNode Reg16Signed(RegisterIndex registerIndex) {
        return _typeConversion.ToSigned(Reg16(registerIndex));
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

    public ValueNode Reg32Signed(RegisterIndex registerIndex) {
        return _typeConversion.ToSigned(Reg32(registerIndex));
    }

    public ValueNode Accumulator(DataType dataType) {
        return new RegisterNode(dataType, (int)RegisterIndex.AxIndex);
    }

    /// <summary>
    /// Gets the stack pointer register node for the given address type.
    /// For 16-bit addressing, returns SP (16-bit register).
    /// For 32-bit addressing, returns ESP (32-bit register).
    /// </summary>
    public ValueNode StackPointer(DataType addressType) {
        return addressType == DataType.UINT16 ? Reg16(RegisterIndex.SpIndex) : Reg32(RegisterIndex.SpIndex);
    }

    /// <summary>
    /// Gets a segment register node for the given segment register index.
    /// </summary>
    public ValueNode SegmentRegister(SegmentRegisterIndex registerIndex) {
        return SReg(registerIndex);
    }

    public ValueNode Reg(DataType dataType, RegisterIndex registerIndex) {
        return Reg(dataType, (int)registerIndex);
    }

    public ValueNode Reg(DataType dataType, int registerIndex) {
        return new RegisterNode(dataType, registerIndex);
    }

}