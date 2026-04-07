namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class Sahf : CfgInstruction {
    public Sahf(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField, 1) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.SAHF);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // EFLAGS(SF:ZF:0:AF:0:PF:1:CF) := AH;
        helper.State.SignFlag = (helper.State.AH & Flags.Sign) == Flags.Sign;
        helper.State.ZeroFlag = (helper.State.AH & Flags.Zero) == Flags.Zero;
        helper.State.AuxiliaryFlag = (helper.State.AH & Flags.Auxiliary) == Flags.Auxiliary;
        helper.State.ParityFlag = (helper.State.AH & Flags.Parity) == Flags.Parity;
        helper.State.CarryFlag = (helper.State.AH & Flags.Carry) == Flags.Carry;
        helper.MoveIpToEndOfInstruction(this);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        ValueNode ah = builder.Register.Reg8H(RegisterIndex.AxIndex);
        ValueNode flags32 = builder.Flag.FlagsRegister(DataType.UINT32);
        ValueNode preservedUpperFlags = new BinaryOperationNode(
            DataType.UINT32,
            flags32,
            BinaryOperation.BITWISE_AND,
            builder.Constant.ToNode(0xFFFFFF00u));
        ValueNode ahAsUInt32 = builder.TypeConversion.Convert(DataType.UINT32, ah);
        ValueNode mergedFlags32 = new BinaryOperationNode(
            DataType.UINT32,
            preservedUpperFlags,
            BinaryOperation.BITWISE_OR,
            ahAsUInt32);

        return builder.WithIpAdvancement(
            this,
            builder.Assign(DataType.UINT32, flags32, mergedFlags32));
    }
}