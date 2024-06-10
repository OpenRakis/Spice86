namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class InstructionWithValueFieldAndRegisterIndex<T> : InstructionWithValueField<T>, IInstructionWithRegisterIndex where T : INumberBase<T> {
    public InstructionWithValueFieldAndRegisterIndex(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<T> valueField,
        int registerIndex) :
        base(address, opcodeField, prefixes, valueField) {
        RegisterIndex = registerIndex;
    }

    public int RegisterIndex { get; }
}