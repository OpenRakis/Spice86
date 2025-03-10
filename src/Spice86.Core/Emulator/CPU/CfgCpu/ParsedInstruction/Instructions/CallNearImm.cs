namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class CallNearImm : InstructionWithOffsetField<short> {
    public CallNearImm(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, InstructionField<short> offsetField) : base(address, opcodeField, prefixes,
        offsetField) {
    }
    
    public override void Execute(InstructionExecutionHelper helper) {
        helper.NearCallOffset(this, OffsetField.Value);
    }
}