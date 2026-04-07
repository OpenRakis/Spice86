namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class Aaa : CfgInstruction {
    public Aaa(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField, 1) {
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.AAA);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        ValueNode al = builder.Register.Reg8(RegisterIndex.AxIndex);
        ValueNode ax = builder.Register.Reg16(RegisterIndex.AxIndex);
        VariableDeclarationNode finalAuxDeclaration = builder.DeclareVariable(DataType.BOOL, "finalAuxiliaryFlag", new ConstantNode(DataType.BOOL, 0));
        VariableDeclarationNode finalCarryDeclaration = builder.DeclareVariable(DataType.BOOL, "finalCarryFlag", new ConstantNode(DataType.BOOL, 0));

        ValueNode lowNibble = new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.BITWISE_AND, builder.Constant.ToNode((byte)0x0F));
        ValueNode lowNibbleGreaterThan9 = new BinaryOperationNode(DataType.BOOL, lowNibble, BinaryOperation.GREATER_THAN, builder.Constant.ToNode((byte)9));
        ValueNode adjustCondition = new BinaryOperationNode(DataType.BOOL, lowNibbleGreaterThan9, BinaryOperation.LOGICAL_OR, builder.Flag.Auxiliary());

        BlockNode adjustTrueCase = new BlockNode(
            builder.Assign(DataType.UINT16, ax, new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.PLUS, builder.Constant.ToNode((ushort)0x0106))),
            builder.Assign(DataType.BOOL, finalAuxDeclaration.Reference, new ConstantNode(DataType.BOOL, 1)),
            builder.Assign(DataType.BOOL, finalCarryDeclaration.Reference, new ConstantNode(DataType.BOOL, 1)));
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