namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithSegmentRegisterIndex: CfgInstruction, IInstructionWithSegmentRegisterIndex {
    protected InstructionWithSegmentRegisterIndex(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes, int segmentRegisterIndex) : base(address, opcodeField, prefixes) {
        SegmentRegisterIndex = segmentRegisterIndex;
    }
    
    public int SegmentRegisterIndex { get; }
}