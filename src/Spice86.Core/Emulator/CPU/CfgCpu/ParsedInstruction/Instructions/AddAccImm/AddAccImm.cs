namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRegImm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class AddAccImm<T> : CfgInstruction, IInstructionWithValueField<T> where T : IUnsignedNumber<T> {
    public AddAccImm(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<T> valueField) :
        base(address, opcodeField, prefixes) {
        ValueField = valueField;
        FieldsInOrder.Add(ValueField);
    }

    public InstructionField<T> ValueField { get; }
}