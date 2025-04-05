namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;

public class InterruptRet : CfgInstruction, IReturnInstruction {
    public InterruptRet(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }

    public CfgInstruction? CurrentCorrespondingCallInstruction { get; set; }

    public override bool CanCauseContextRestore => true;

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleInterruptRet(this);
    }

    public override InstructionNode ToAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.IRET);
    }
}