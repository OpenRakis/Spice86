namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class InstructionWithValueField<T> : CfgInstruction, IInstructionWithValueField<T> where T : INumberBase<T> {
    public InstructionWithValueField(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<T> valueField) :
        base(address, opcodeField, prefixes) {
        ValueField = valueField;
        FieldsInOrder.Add(ValueField);
    }

    public InstructionField<T> ValueField { get; }
}