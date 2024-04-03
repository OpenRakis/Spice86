namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp1;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class Grp1<T> : InstructionWithModRm, IInstructionWithValueField<T> where T : INumberBase<T> {
    public Grp1(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes,
        ModRmContext modRmContext,
        InstructionField<T> valueField) : base(address, opcodeField, prefixes, modRmContext) {
        ValueField = valueField;
        FieldsInOrder.Add(valueField);
    }

    public InstructionField<T> ValueField { get; }
}