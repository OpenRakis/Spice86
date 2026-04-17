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

    /// <summary>
    /// Gets a register node by register name (e.g., "AL", "AH", "AX", "EAX", "DX", "EDX").
    /// Used by mixin templates to convert register name parameters to AST nodes.
    /// </summary>
    public ValueNode RegByName(string registerName) {
        return registerName switch {
            // 8-bit low registers
            "AL" => Reg8L(RegisterIndex.AxIndex),
            "CL" => Reg8L(RegisterIndex.CxIndex),
            "DL" => Reg8L(RegisterIndex.DxIndex),
            "BL" => Reg8L(RegisterIndex.BxIndex),

            // 8-bit high registers
            "AH" => Reg8H(RegisterIndex.AxIndex),
            "CH" => Reg8H(RegisterIndex.CxIndex),
            "DH" => Reg8H(RegisterIndex.DxIndex),
            "BH" => Reg8H(RegisterIndex.BxIndex),

            // 16-bit registers
            "AX" => Reg16(RegisterIndex.AxIndex),
            "CX" => Reg16(RegisterIndex.CxIndex),
            "DX" => Reg16(RegisterIndex.DxIndex),
            "BX" => Reg16(RegisterIndex.BxIndex),
            "SP" => Reg16(RegisterIndex.SpIndex),
            "BP" => Reg16(RegisterIndex.BpIndex),
            "SI" => Reg16(RegisterIndex.SiIndex),
            "DI" => Reg16(RegisterIndex.DiIndex),

            // 32-bit registers
            "EAX" => Reg32(RegisterIndex.AxIndex),
            "ECX" => Reg32(RegisterIndex.CxIndex),
            "EDX" => Reg32(RegisterIndex.DxIndex),
            "EBX" => Reg32(RegisterIndex.BxIndex),
            "ESP" => Reg32(RegisterIndex.SpIndex),
            "EBP" => Reg32(RegisterIndex.BpIndex),
            "ESI" => Reg32(RegisterIndex.SiIndex),
            "EDI" => Reg32(RegisterIndex.DiIndex),

            _ => throw new ArgumentException($"Unknown register name: {registerName}", nameof(registerName))
        };
    }

    /// <summary>
    /// Gets a segment register node by segment register name (e.g., "ES", "DS", "SS", "FS", "GS").
    /// Used by mixin templates to convert segment register name parameters to AST nodes.
    /// </summary>
    public ValueNode SRegByName(string segmentRegisterName) {
        return segmentRegisterName switch {
            "ES" => SReg(SegmentRegisterIndex.EsIndex),
            "CS" => SReg(SegmentRegisterIndex.CsIndex),
            "SS" => SReg(SegmentRegisterIndex.SsIndex),
            "DS" => SReg(SegmentRegisterIndex.DsIndex),
            "FS" => SReg(SegmentRegisterIndex.FsIndex),
            "GS" => SReg(SegmentRegisterIndex.GsIndex),
            _ => throw new ArgumentException($"Unknown segment register name: {segmentRegisterName}", nameof(segmentRegisterName))
        };
    }
}