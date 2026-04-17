namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class Lahf : CfgInstruction {
    public Lahf(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField, 1) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.LAHF);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.State.AH = (byte)helper.State.Flags.FlagRegister;
        helper.MoveIpToEndOfInstruction(this);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        ValueNode ah = builder.Register.Reg8H(RegisterIndex.AxIndex);
        ValueNode flags = builder.Flag.FlagsRegister(DataType.UINT16);
        ValueNode flagsAsByte = builder.TypeConversion.Convert(DataType.UINT8, flags);
        return builder.WithIpAdvancement(this, builder.Assign(DataType.UINT8, ah, flagsAsByte));
    }
}