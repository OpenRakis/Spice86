namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class Aam : InstructionWithValueField<byte> {
    public Aam(SegmentedAddress address, InstructionField<ushort> opcodeField, InstructionField<byte> valueField) :
        base(address, opcodeField, new List<InstructionPrefix>(), valueField, 1) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.AAM);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        ValueNode? valueNode = builder.InstructionField.ToNode(ValueField);
        if (valueNode == null) {
            throw new InvalidOperationException("AAM value field cannot be null");
        }

        ValueNode al = builder.Register.Reg8(RegisterIndex.AxIndex);
        ValueNode ah = builder.Register.Reg8H(RegisterIndex.AxIndex);
        VariableDeclarationNode valueDeclaration = builder.DeclareVariable(DataType.UINT8, "v2", valueNode);
        VariableDeclarationNode alDeclaration = builder.DeclareVariable(DataType.UINT8, "v1", al);
        VariableDeclarationNode resultDeclaration = builder.DeclareVariable(
            DataType.UINT8,
            "result",
            new BinaryOperationNode(DataType.UINT8, alDeclaration.Reference, BinaryOperation.MODULO, valueDeclaration.Reference));

        MethodCallNode updateFlags = new MethodCallNode("Alu8", "UpdateFlags", resultDeclaration.Reference);

        ValueNode isZeroCondition = new BinaryOperationNode(DataType.BOOL, valueDeclaration.Reference, BinaryOperation.EQUAL, builder.Constant.ToNode((byte)0));
        IfElseNode divisionCheck = builder.ControlFlow.ThrowIf<CpuDivisionErrorException>(isZeroCondition, "Division by zero");

        return builder.WithIpAdvancement(
            this,
            valueDeclaration,
            alDeclaration,
            divisionCheck,
            resultDeclaration,
            builder.Assign(DataType.UINT8, ah, new BinaryOperationNode(DataType.UINT8, alDeclaration.Reference, BinaryOperation.DIVIDE, valueDeclaration.Reference)),
            builder.Assign(DataType.UINT8, al, resultDeclaration.Reference),
            updateFlags);
    }
}