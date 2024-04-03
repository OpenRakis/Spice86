namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRmImm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class MovRmImm<T> : InstructionWithModRm, IInstructionWithValueField<T> where T : IUnsignedNumber<T>  {
    protected MovRmImm(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes,
        ModRmContext modRmContext, InstructionField<T> valueField) : base(address, opcodeField, prefixes, modRmContext) {
        ValueField = valueField;
        FieldsInOrder.Add(ValueField);
    }

    public InstructionField<T> ValueField { get; }
}