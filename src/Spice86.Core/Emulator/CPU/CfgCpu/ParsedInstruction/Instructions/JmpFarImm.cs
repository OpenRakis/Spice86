namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;

public class JmpFarImm : CfgInstruction, IInstructionWithOffsetField, IInstructionWithSegmentField {
    public JmpFarImm(
        SegmentedAddress address,
        InstructionField<byte> opcodeField,
        InstructionField<ushort> offsetField,
        InstructionField<ushort> segmentField) :
        base(address, opcodeField) {
        OffsetField = offsetField;
        SegmentField = segmentField;
        FieldsInOrder.Add(OffsetField);
        FieldsInOrder.Add(SegmentField);
    }

    public InstructionField<ushort> OffsetField { get; }
    public InstructionField<ushort> SegmentField { get; }

    public override void Execute(InstructionExecutionHelper helper) {
        ushort offset = helper.OffsetValue(this);
        ushort segment = helper.SegmentValue(this);
        helper.JumpFar(this, segment, offset);
    }
}