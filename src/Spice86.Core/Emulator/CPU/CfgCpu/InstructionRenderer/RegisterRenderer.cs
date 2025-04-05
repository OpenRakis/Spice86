namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Frozen;

public class RegisterRenderer {
    private static readonly FrozenDictionary<int, string> _registersNames = new Dictionary<int, string>() {
        { (int)RegisterIndex.AxIndex, "AX" },
        { (int)RegisterIndex.CxIndex, "CX" },
        { (int)RegisterIndex.DxIndex, "DX" },
        { (int)RegisterIndex.BxIndex, "BX" },
        { (int)RegisterIndex.SpIndex, "SP" },
        { (int)RegisterIndex.BpIndex, "BP" },
        { (int)RegisterIndex.SiIndex, "SI" },
        { (int)RegisterIndex.DiIndex, "DI" }
    }.ToFrozenDictionary();

    
    private static readonly FrozenDictionary<int, string> _segmentRegistersNames = new Dictionary<int, string>() {
        { (int)SegmentRegisterIndex.EsIndex, "ES" },
        { (int)SegmentRegisterIndex.CsIndex, "CS" },
        { (int)SegmentRegisterIndex.SsIndex, "SS" },
        { (int)SegmentRegisterIndex.DsIndex, "DS" },
        { (int)SegmentRegisterIndex.FsIndex, "FS" },
        { (int)SegmentRegisterIndex.GsIndex, "GS" }
    }.ToFrozenDictionary();

    private string Reg8Name(int regIndex) {
        string suffix = UInt8HighLowRegistersIndexer.IsHigh((uint)regIndex) ? "H" : "L";
        string reg16 = Reg16Name((int)UInt8HighLowRegistersIndexer.ComputeRegisterIndexInArray((uint)regIndex));
        return $"{reg16[..1]}{suffix}";
    }

    private string Reg16Name(int regIndex) {
        return _registersNames[regIndex];
    }
    
    private string Reg32Name(int regIndex) {
        return "E" + Reg16Name(regIndex);
    }

    public string ToStringRegister(BitWidth bitWidth, int registerIndex) {
        return bitWidth switch {
            BitWidth.BYTE_8=> Reg8Name(registerIndex),
            BitWidth.WORD_16 => Reg16Name(registerIndex),
            BitWidth.DWORD_32 => Reg32Name(registerIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(bitWidth), bitWidth, null)
        };
    }

    public string ToStringSegmentRegister(int registerIndex) {
        return _segmentRegistersNames[registerIndex];
    }
}