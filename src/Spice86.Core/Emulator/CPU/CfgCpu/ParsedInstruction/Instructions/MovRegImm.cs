namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class MovRegImm<T> : CfgInstruction where T : IUnsignedNumber<T> {
    public MovRegImm(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<T> valueField,
        int regIndex) :
        base(address, opcodeField, prefixes) {
        ValueField = valueField;
        FieldsInOrder.Add(ValueField);
        
        RegIndex = regIndex;
    }

    public InstructionField<T> ValueField { get; }
    public int RegIndex { get; }
}