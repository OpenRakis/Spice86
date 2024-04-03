namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovMoffsAcc;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class MovMoffsAcc : CfgInstruction, IInstructionWithSegmentRegisterIndexAndOffsetField {
    protected MovMoffsAcc(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes, uint segmentRegisterIndex, InstructionField<ushort> offsetField) : base(address, opcodeField, prefixes) {
        SegmentRegisterIndex = segmentRegisterIndex;
        OffsetField = offsetField;
        FieldsInOrder.Add(offsetField);
    }
    
    public uint SegmentRegisterIndex { get; }
    public InstructionField<ushort> OffsetField { get; }
}