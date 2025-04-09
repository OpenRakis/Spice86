namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp5RmCallNear : InstructionWithModRm {
    public Grp5RmCallNear(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes,
        modRmContext) {
    }
    
    public override void Execute(InstructionExecutionHelper helper) {
        helper.ModRm.RefreshWithNewModRmContext(ModRmContext);
        ushort targetIp = helper.ModRm.RM16;
        helper.NearCallWithReturnIpNextInstruction(this, targetIp);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CALL_NEAR, builder.ModRm.RmToNode(DataType.UINT16, ModRmContext));
    }
}