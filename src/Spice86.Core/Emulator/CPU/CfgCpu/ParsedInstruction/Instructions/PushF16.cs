namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class PushF16 : CfgInstruction {
    public PushF16(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes) {
    }
    
    public override void Execute(InstructionExecutionHelper helper) {
        helper.Stack.Push16((ushort)helper.State.Flags.FlagRegister);
        helper.MoveIpAndSetNextNode(this);
    }
}