namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Cwd32 : CfgInstruction {
    public Cwd32(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // CWD, Sign extend EAX into EDX (dword to qword)
        if (helper.State.EAX >= 0x80000000) {
            helper.State.EDX = 0xFFFFFFFF;
        } else {
            helper.State.EDX = 0;
        }
        helper.MoveIpAndSetNextNode(this);
    }
}