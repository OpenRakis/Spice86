namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class Aaa : CfgInstruction {
    public Aaa(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        bool finalAuxillaryFlag = false;
        bool finalCarryFlag = false;
        if ((helper.State.AL & 0x0F) > 9 || helper.State.AuxiliaryFlag) {
            helper.State.AX = (ushort)(helper.State.AX + 0x106);
            finalAuxillaryFlag = true;
            finalCarryFlag = true;
        }

        helper.State.AL = (byte)(helper.State.AL & 0x0F);
        // Undocumented behaviour
        helper.Alu8.UpdateFlags(helper.State.AL);
        helper.State.AuxiliaryFlag = finalAuxillaryFlag;
        helper.State.CarryFlag = finalCarryFlag;
        helper.MoveIpAndSetNextNode(this);
    }
}