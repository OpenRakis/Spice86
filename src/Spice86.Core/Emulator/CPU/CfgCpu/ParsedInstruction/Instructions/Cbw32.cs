namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Cbw32 : CfgInstruction {
    public Cbw32(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // CBW, Convert word to dword
        int shortValue = (short)helper.State.AX;
        helper.State.EAX = (uint)shortValue;
        helper.MoveIpAndSetNextNode(this);
    }
}