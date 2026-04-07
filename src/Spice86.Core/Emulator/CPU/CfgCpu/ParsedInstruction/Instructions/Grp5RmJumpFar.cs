namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

public class Grp5RmJumpFar : InstructionWithModRm, IJumpInstruction {
    public Grp5RmJumpFar(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes,
        modRmContext, null) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.JMP_FAR, builder.ModRm.ToMemoryAddressNode(DataType.UINT32, ModRmContext));
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.ModRm.RefreshWithNewModRmContext(ModRmContext);
        uint ipAddress = helper.ModRm.MandatoryMemoryAddress;
        (ushort cs, ushort ip) = helper.Memory.SegmentedAddress16[ipAddress];
        helper.JumpFar(this, cs, ip);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        SegmentedAddressNode targetAddress = builder.ModRm.ToSegmentedAddressNode(16, ModRmContext);
        return new JumpFarNode(this, targetAddress);
    }
}