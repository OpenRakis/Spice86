namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class InstructionWithValueField<T> : CfgInstruction, IInstructionWithValueField<T> where T : INumberBase<T> {
    protected InstructionWithValueField(SegmentedAddress address,
        InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<T> valueField) :
        base(address, opcodeField, prefixes) {
        ValueField = valueField;
        AddField(ValueField);
    }
    
    protected InstructionWithValueField(SegmentedAddress address, InstructionField<ushort> opcodeField, InstructionField<T> valueField) : this(address,
        opcodeField, new List<InstructionPrefix>(), valueField) {
    }

    public InstructionField<T> ValueField { get; }
}