namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class InstructionWithOffsetField<T> : CfgInstruction, IInstructionWithOffsetField<T> where T : INumberBase<T> {
    public InstructionWithOffsetField(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes, InstructionField<T> offsetField, int? maxSuccessorsCount) :
        base(address, opcodeField, prefixes, maxSuccessorsCount) {
        OffsetField = offsetField;
        AddField(OffsetField);
    }

    public InstructionField<T> OffsetField { get; }
}