namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp45;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp4Callback : InstructionWithModRm {
    public Grp4Callback(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes,
        ModRmContext modRmContext, InstructionField<byte> callbackNumber) : base(address, opcodeField, prefixes,
        modRmContext) {
        CallbackNumber = callbackNumber;
        FieldsInOrder.Add(callbackNumber);
    }

    public InstructionField<byte> CallbackNumber { get; }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}