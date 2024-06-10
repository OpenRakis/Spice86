namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithRegisterIndex : CfgInstruction, IInstructionWithRegisterIndex {
    protected InstructionWithRegisterIndex(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        int registerIndex) :
        base(address, opcodeField, prefixes) {
        RegisterIndex = registerIndex;
    }
    public int RegisterIndex { get; }
}