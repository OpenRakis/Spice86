namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Cwd16 : CfgInstruction {
    public Cwd16(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // CWD, Sign extend AX into DX (word to dword)
        if (helper.State.AX >= 0x8000) {
            helper.State.DX = 0xFFFF;
        } else {
            helper.State.DX = 0;
        }
        helper.MoveIpAndSetNextNode(this);
    }
}