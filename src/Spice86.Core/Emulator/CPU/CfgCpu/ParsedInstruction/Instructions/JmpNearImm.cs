namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class JmpNearImm<T> : CfgInstruction where T : ISignedNumber<T> {
    public JmpNearImm(SegmentedAddress address, InstructionField<byte> opcodeField, InstructionField<T> offsetField) :
        base(address, opcodeField) {
        OffsetField = offsetField;
        FieldsInOrder.Add(OffsetField);
    }

    public InstructionField<T> OffsetField { get; }
}