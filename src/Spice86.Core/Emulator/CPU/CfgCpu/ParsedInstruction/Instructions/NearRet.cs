namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.AST.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;

public class NearRet : CfgInstruction, IReturnInstruction {

    public NearRet(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }
    
    public CfgInstruction? CurrentCorrespondingCallInstruction { get; set; }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleNearRet(this, 0);
    }

    public override InstructionNode ToAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.RET_NEAR);
    }
}