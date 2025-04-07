namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class InstructionWithModRmAndValueField<T> : InstructionWithModRm, IInstructionWithValueField<T> where T : INumberBase<T>  {
    protected InstructionWithModRmAndValueField(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes,
        ModRmContext modRmContext, InstructionField<T> valueField) : base(address, opcodeField, prefixes, modRmContext) {
        ValueField = valueField;
        AddField(ValueField);
    }

    public InstructionField<T> ValueField { get; }
}