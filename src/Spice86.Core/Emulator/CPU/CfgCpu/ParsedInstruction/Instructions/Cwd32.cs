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

public class Cwd32 : CfgInstruction {
    public Cwd32(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // CWD, Sign extend EAX into EDX (dword to qword)
        short axSigned = (short)helper.State.EAX;
        short sign = (short)(axSigned >> 31);
        helper.State.EDX = (ushort)sign;
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CDQ);
    }

    public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
        // EDX = (UINT32)((INT32)EAX_SIGNED >> 31) - arithmetic right shift to replicate sign bit
        ValueNode edxNode = builder.Register.Reg32(RegisterIndex.DxIndex);
        ValueNode eaxSigned = builder.Register.Reg32Signed(RegisterIndex.AxIndex);
        BinaryOperationNode shiftRight = new BinaryOperationNode(DataType.INT32, eaxSigned, BinaryOperation.RIGHT_SHIFT, builder.Constant.ToNode(31));
        ValueNode result = builder.TypeConversion.Convert(DataType.UINT32, shiftRight);
        return builder.WithIpAdvancement(this, builder.Assign(DataType.UINT32, edxNode, result));
    }
}