namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithModRm : CfgInstruction {

    public InstructionWithModRm(SegmentedAddress address, InstructionField<byte> opcodeField, ModRmContext modRmContext) : this(address, opcodeField, new List<InstructionPrefix>(), modRmContext) {
    }
    public InstructionWithModRm(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes) {
        ModRmContext = modRmContext;
        FieldsInOrder.AddRange(ModRmContext.FieldsInOrder);
    }
    public ModRmContext ModRmContext { get; init; }
}