namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithSegmentRegisterIndex: CfgInstruction, IInstructionWithSegmentRegisterIndex {
    protected InstructionWithSegmentRegisterIndex(
        SegmentedAddress address,
        InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes,
        int segmentRegisterIndex,
        int defaultSegmentRegisterIndex,
        int? maxSuccessorsCount) : base(address, opcodeField, prefixes, maxSuccessorsCount) {
        SegmentRegisterIndex = segmentRegisterIndex;
        DefaultSegmentRegisterIndex = defaultSegmentRegisterIndex;
    }
    
    public int SegmentRegisterIndex { get; }
    public int DefaultSegmentRegisterIndex { get; }
}