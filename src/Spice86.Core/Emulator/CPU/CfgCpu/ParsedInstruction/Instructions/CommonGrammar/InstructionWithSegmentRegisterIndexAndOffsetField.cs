namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithSegmentRegisterIndexAndOffsetField<T>: CfgInstruction, IInstructionWithSegmentRegisterIndex, IInstructionWithOffsetField<T> {
    protected InstructionWithSegmentRegisterIndexAndOffsetField(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes, int segmentRegisterIndex, InstructionField<T> offsetField) : base(address, opcodeField, prefixes) {
        SegmentRegisterIndex = segmentRegisterIndex;
        OffsetField = offsetField;
        FieldsInOrder.Add(offsetField);
    }
    
    public int SegmentRegisterIndex { get; }
    public InstructionField<T> OffsetField { get; }
}