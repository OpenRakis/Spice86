namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRegImm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class MovRegImm<T> : CfgInstruction, IInstructionWithRegisterIndex, IInstructionWithValueField<T> where T : IUnsignedNumber<T> {
    public MovRegImm(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<T> valueField,
        int registerIndex) :
        base(address, opcodeField, prefixes) {
        ValueField = valueField;
        FieldsInOrder.Add(ValueField);
        
        RegisterIndex = registerIndex;
    }

    public InstructionField<T> ValueField { get; }
    public int RegisterIndex { get; }
}