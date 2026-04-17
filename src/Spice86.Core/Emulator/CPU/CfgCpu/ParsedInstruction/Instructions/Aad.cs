namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

public class Aad : InstructionWithValueField<byte> {
    public Aad(SegmentedAddress address, InstructionField<ushort> opcodeField, InstructionField<byte> valueField) :
        base(address, opcodeField, new List<InstructionPrefix>(), valueField, 1) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.AAD);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        byte v2 = helper.InstructionFieldValueRetriever.GetFieldValue(ValueField);
        helper.State.AL = (byte)(helper.State.AL + (helper.State.AH * v2));
        helper.State.AH = 0;
        helper.Alu8.UpdateFlags(helper.State.AL);
        // Undefined behaviour
        helper.State.CarryFlag = false;
        helper.State.AuxiliaryFlag = false;
        helper.State.OverflowFlag = false;
        helper.MoveIpToEndOfInstruction(this);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        ValueNode? valueNode = builder.InstructionField.ToNode(ValueField);
        if (valueNode == null) {
            throw new InvalidOperationException("AAD value field cannot be null");
        }

        ValueNode al = builder.Register.Reg8(RegisterIndex.AxIndex);
        ValueNode ah = builder.Register.Reg8H(RegisterIndex.AxIndex);
        VariableDeclarationNode valueDeclaration = builder.DeclareVariable(DataType.UINT8, "v2", valueNode);

        ValueNode mul = new BinaryOperationNode(DataType.UINT8, ah, BinaryOperation.MULTIPLY, valueDeclaration.Reference);
        ValueNode sum = new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.PLUS, mul);
        MethodCallNode updateFlags = new MethodCallNode("Alu8", "UpdateFlags", al);

        return builder.WithIpAdvancement(
            this,
            valueDeclaration,
            builder.Assign(DataType.UINT8, al, sum),
            builder.Assign(DataType.UINT8, ah, builder.Constant.ToNode((byte)0)),
            // Undocumented behaviour
            updateFlags,
            builder.Assign(DataType.BOOL, builder.Flag.Carry(), builder.Constant.ToNode(false)),
            builder.Assign(DataType.BOOL, builder.Flag.Auxiliary(), builder.Constant.ToNode(false)),
            builder.Assign(DataType.BOOL, builder.Flag.Overflow(), builder.Constant.ToNode(false)));
    }
}