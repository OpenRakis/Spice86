namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class Cwd16 : CfgInstruction {
    public Cwd16(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // CWD, Sign extend AX into DX (word to dword)
        short axSigned = (short)helper.State.AX;
        short sign = (short)(axSigned >> 15);
        helper.State.DX = (ushort)sign;
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CWD);
    }

    public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
        // DX = (UINT16)(AX_SIGNED >> 15) - arithmetic right shift to replicate sign bit
        ValueNode dxNode = builder.Register.Reg16(RegisterIndex.DxIndex);
        ValueNode axSigned = builder.Register.Reg16Signed(RegisterIndex.AxIndex);
        BinaryOperationNode shiftRight = new BinaryOperationNode(DataType.INT16, axSigned, BinaryOperation.RIGHT_SHIFT, builder.Constant.ToNode(15));
        ValueNode result = builder.TypeConversion.Convert(DataType.UINT16, shiftRight);
        return builder.WithIpAdvancement(this, builder.Assign(DataType.UINT16, dxNode, result));
    }
}