namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class Sahf : CfgInstruction {
    public Sahf(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // EFLAGS(SF:ZF:0:AF:0:PF:1:CF) := AH;
        helper.State.SignFlag = (helper.State.AH & Flags.Sign) == Flags.Sign;
        helper.State.ZeroFlag = (helper.State.AH & Flags.Zero) == Flags.Zero;
        helper.State.AuxiliaryFlag = (helper.State.AH & Flags.Auxiliary) == Flags.Auxiliary;
        helper.State.ParityFlag = (helper.State.AH & Flags.Parity) == Flags.Parity;
        helper.State.CarryFlag = (helper.State.AH & Flags.Carry) == Flags.Carry;
        helper.MoveIpAndSetNextNode(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.SAHF);
    }
}