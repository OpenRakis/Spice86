namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithSegmentRegisterIndexAndOffsetField<T>: CfgInstruction, IInstructionWithSegmentRegisterIndex, IInstructionWithOffsetField<T> {
    protected InstructionWithSegmentRegisterIndexAndOffsetField(
        SegmentedAddress address,
        InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes,
        int segmentRegisterIndex,
        int defaultSegmentRegisterIndex,
        InstructionField<T> offsetField,
        int? maxSuccessorsCount) : base(address, opcodeField, prefixes, maxSuccessorsCount) {
        SegmentRegisterIndex = segmentRegisterIndex;
        DefaultSegmentRegisterIndex = defaultSegmentRegisterIndex;
        OffsetField = offsetField;
        AddField(offsetField);
    }
    
    public int SegmentRegisterIndex { get; }
    public int DefaultSegmentRegisterIndex { get; }
    public InstructionField<T> OffsetField { get; }
}