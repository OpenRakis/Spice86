namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithRegisterIndex : CfgInstruction, IInstructionWithRegisterIndex {
    protected InstructionWithRegisterIndex(SegmentedAddress address,
        InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes,
        int registerIndex, int? maxSuccessorsCount) :
        base(address, opcodeField, prefixes, maxSuccessorsCount) {
        RegisterIndex = registerIndex;
    }
    public int RegisterIndex { get; }
}