namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class Daa : CfgInstruction {
    public Daa(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        byte initialAL = helper.State.AL;
        bool initialCF = helper.State.CarryFlag;
        bool finalAuxillaryFlag = false;
        if ((helper.State.AL & 0x0F) > 9 || helper.State.AuxiliaryFlag) {
            helper.State.AL = (byte)(helper.State.AL + 6);
            finalAuxillaryFlag = true;
        }

        bool finalCarryFlag;
        if (initialAL > 0x99 || initialCF) {
            helper.State.AL = (byte)(helper.State.AL + 0x60);
            finalCarryFlag = true;
        } else {
            finalCarryFlag = false;
        }

        // Undocumented behaviour
        helper.Alu8.UpdateFlags(helper.State.AL);
        helper.State.AuxiliaryFlag = finalAuxillaryFlag;
        helper.State.CarryFlag = finalCarryFlag;
        helper.MoveIpAndSetNextNode(this);
    }
}