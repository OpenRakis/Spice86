namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class Das : CfgInstruction {
    public Das(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        byte initialAL = helper.State.AL;
        bool initialCF = helper.State.CarryFlag;
        bool finalAuxillaryFlag = false;
        bool finalCarryFlag = false;
        helper.State.CarryFlag = false;
        if ((helper.State.AL & 0x0F) > 9 || helper.State.AuxiliaryFlag) {
            helper.State.AL = (byte)(helper.State.AL - 6);
            finalCarryFlag = helper.State.CarryFlag || initialCF;
            finalAuxillaryFlag = true;
        }

        if (initialAL > 0x99 || initialCF) {
            helper.State.AL = (byte)(helper.State.AL - 0x60);
            finalCarryFlag = true;
        }

        // Undocumented behaviour
        helper.Alu8.UpdateFlags(helper.State.AL);
        helper.State.AuxiliaryFlag = finalAuxillaryFlag;
        helper.State.CarryFlag = finalCarryFlag;
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.DAS);
    }

    public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
        ValueNode al = builder.Register.Reg8(RegisterIndex.AxIndex);
        VariableDeclarationNode initialAlDeclaration = builder.DeclareVariable(DataType.UINT8, "initialAl", al);
        VariableDeclarationNode initialCfDeclaration = builder.DeclareVariable(DataType.BOOL, "initialCf", builder.Flag.Carry());
        VariableDeclarationNode finalAuxDeclaration = builder.DeclareVariable(DataType.BOOL, "finalAuxiliaryFlag", new ConstantNode(DataType.BOOL, 0));
        VariableDeclarationNode finalCarryDeclaration = builder.DeclareVariable(DataType.BOOL, "finalCarryFlag", new ConstantNode(DataType.BOOL, 0));

        ValueNode lowNibble = new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.BITWISE_AND, builder.Constant.ToNode((byte)0x0F));
        ValueNode lowNibbleGreaterThan9 = new BinaryOperationNode(DataType.BOOL, lowNibble, BinaryOperation.GREATER_THAN, builder.Constant.ToNode((byte)9));
        ValueNode adjustLowCondition = new BinaryOperationNode(DataType.BOOL, lowNibbleGreaterThan9, BinaryOperation.LOGICAL_OR, builder.Flag.Auxiliary());

        BlockNode adjustLowTrueCase = new BlockNode(
            builder.Assign(DataType.UINT8, al, new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.MINUS, builder.Constant.ToNode((byte)6))),
            builder.Assign(
                DataType.BOOL,
                finalCarryDeclaration.Reference,
                new BinaryOperationNode(DataType.BOOL, builder.Flag.Carry(), BinaryOperation.LOGICAL_OR, initialCfDeclaration.Reference)),
            builder.Assign(DataType.BOOL, finalAuxDeclaration.Reference, new ConstantNode(DataType.BOOL, 1)));
        IfElseNode adjustLowIf = new IfElseNode(adjustLowCondition, adjustLowTrueCase, new BlockNode());

        ValueNode adjustCarryCondition = new BinaryOperationNode(
            DataType.BOOL,
            new BinaryOperationNode(DataType.BOOL, initialAlDeclaration.Reference, BinaryOperation.GREATER_THAN, builder.Constant.ToNode((byte)0x99)),
            BinaryOperation.LOGICAL_OR,
            initialCfDeclaration.Reference);

        BlockNode adjustCarryTrueCase = new BlockNode(
            builder.Assign(DataType.UINT8, al, new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.MINUS, builder.Constant.ToNode((byte)0x60))),
            builder.Assign(DataType.BOOL, finalCarryDeclaration.Reference, new ConstantNode(DataType.BOOL, 1)));
        IfElseNode adjustCarryIf = new IfElseNode(adjustCarryCondition, adjustCarryTrueCase, new BlockNode());

        MethodCallNode updateFlags = new MethodCallNode("Alu8", "UpdateFlags", al);

        return builder.WithIpAdvancement(
            this,
            initialAlDeclaration,
            initialCfDeclaration,
            finalAuxDeclaration,
            finalCarryDeclaration,
            builder.Assign(DataType.BOOL, builder.Flag.Carry(), new ConstantNode(DataType.BOOL, 0)),
            adjustLowIf,
            adjustCarryIf,
            // Undocumented behaviour
            updateFlags,
            builder.Assign(DataType.BOOL, builder.Flag.Auxiliary(), finalAuxDeclaration.Reference),
            builder.Assign(DataType.BOOL, builder.Flag.Carry(), finalCarryDeclaration.Reference));
    }
}