namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

public class Aas : CfgInstruction {
    public Aas(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField, 1) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.AAS);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        bool finalAuxillaryFlag = false;
        bool finalCarryFlag = false;
        if ((helper.State.AL & 0x0F) > 9 || helper.State.AuxiliaryFlag) {
            helper.State.AX = (ushort)(helper.State.AX - 6);
            helper.State.AH = (byte)(helper.State.AH - 1);
            finalAuxillaryFlag = true;
            finalCarryFlag = true;
        }

        helper.State.AL = (byte)(helper.State.AL & 0x0F);
        // Undocumented behaviour
        helper.Alu8.UpdateFlags(helper.State.AL);
        helper.State.AuxiliaryFlag = finalAuxillaryFlag;
        helper.State.CarryFlag = finalCarryFlag;
        helper.MoveIpToEndOfInstruction(this);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        ValueNode al = builder.Register.Reg8(RegisterIndex.AxIndex);
        ValueNode ah = builder.Register.Reg8H(RegisterIndex.AxIndex);
        ValueNode ax = builder.Register.Reg16(RegisterIndex.AxIndex);
        VariableDeclarationNode finalAuxDeclaration = builder.DeclareVariable(DataType.BOOL, "finalAuxiliaryFlag", builder.Constant.ToNode(false));
        VariableDeclarationNode finalCarryDeclaration = builder.DeclareVariable(DataType.BOOL, "finalCarryFlag", builder.Constant.ToNode(false));

        ValueNode lowNibble = new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.BITWISE_AND, builder.Constant.ToNode((byte)0x0F));
        ValueNode lowNibbleGreaterThan9 = new BinaryOperationNode(DataType.BOOL, lowNibble, BinaryOperation.GREATER_THAN, builder.Constant.ToNode((byte)9));
        ValueNode adjustCondition = new BinaryOperationNode(DataType.BOOL, lowNibbleGreaterThan9, BinaryOperation.LOGICAL_OR, builder.Flag.Auxiliary());

        BlockNode adjustTrueCase = new BlockNode(
            builder.Assign(DataType.UINT16, ax, new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.MINUS, builder.Constant.ToNode((ushort)6))),
            builder.Assign(DataType.UINT8, ah, new BinaryOperationNode(DataType.UINT8, ah, BinaryOperation.MINUS, builder.Constant.ToNode((byte)1))),
            builder.Assign(DataType.BOOL, finalAuxDeclaration.Reference, builder.Constant.ToNode(true)),
            builder.Assign(DataType.BOOL, finalCarryDeclaration.Reference, builder.Constant.ToNode(true)));
        IfElseNode adjustIf = new IfElseNode(adjustCondition, adjustTrueCase, new BlockNode());

        MethodCallNode updateFlags = new MethodCallNode("Alu8", "UpdateFlags", al);

        return builder.WithIpAdvancement(
            this,
            finalAuxDeclaration,
            finalCarryDeclaration,
            adjustIf,
            builder.Assign(DataType.UINT8, al, new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.BITWISE_AND, builder.Constant.ToNode((byte)0x0F))),
            // Undocumented behaviour
            updateFlags,
            builder.Assign(DataType.BOOL, builder.Flag.Auxiliary(), finalAuxDeclaration.Reference),
            builder.Assign(DataType.BOOL, builder.Flag.Carry(), finalCarryDeclaration.Reference));
    }
}