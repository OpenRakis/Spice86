namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithModRm : CfgInstruction {
    public InstructionWithModRm(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes) {
        ModRmContext = modRmContext;
        FieldsInOrder.AddRange(ModRmContext.FieldsInOrder);
    }
    public ModRmContext ModRmContext { get; init; }
}