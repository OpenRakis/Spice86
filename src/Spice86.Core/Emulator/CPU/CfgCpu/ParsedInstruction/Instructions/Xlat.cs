namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Xlat : InstructionWithSegmentRegisterIndex {
    public Xlat(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, int segmentRegisterIndex) : base(address, opcodeField, prefixes, segmentRegisterIndex) {
    }
    
    public override void Execute(InstructionExecutionHelper helper) {
        uint address = helper.PhysicalAddress(this, helper.State.BX) + helper.State.AL;
        helper.State.AL = helper.Memory.UInt8[address];
        helper.MoveIpAndSetNextNode(this);
    }
}
