namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>BCD arithmetic: DAA, DAS, AAA, AAS, AAM, AAD</summary>
public class BcdAdjustParser : BaseInstructionParser {
    public BcdAdjustParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseDecimalAdjust(ParsingContext context, BinaryOperation adjustOp, InstructionOperation displayOp) {
        bool isSubtract = adjustOp == BinaryOperation.MINUS;
        CfgInstruction instr = new(context.Address, context.OpcodeField, 1);
        ValueNode al = _astBuilder.Register.Reg8(RegisterIndex.AxIndex);
        VariableDeclarationNode initialAlDeclaration = _astBuilder.DeclareVariable(DataType.UINT8, "initialAl", al);
        VariableDeclarationNode initialCfDeclaration = _astBuilder.DeclareVariable(DataType.BOOL, "initialCf", _astBuilder.Flag.Carry());
        VariableDeclarationNode finalAuxDeclaration = _astBuilder.DeclareVariable(DataType.BOOL, "finalAuxiliaryFlag", _astBuilder.Constant.ToNode(false));
        VariableDeclarationNode finalCarryDeclaration = _astBuilder.DeclareVariable(DataType.BOOL, "finalCarryFlag", _astBuilder.Constant.ToNode(false));
        ValueNode lowNibble = new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.BITWISE_AND, _astBuilder.Constant.ToNode((byte)0x0F));
        ValueNode lowNibbleGreaterThan9 = new BinaryOperationNode(DataType.BOOL, lowNibble, BinaryOperation.GREATER_THAN, _astBuilder.Constant.ToNode((byte)9));
        ValueNode adjustLowCondition = new BinaryOperationNode(DataType.BOOL, lowNibbleGreaterThan9, BinaryOperation.LOGICAL_OR, _astBuilder.Flag.Auxiliary());
        List<IVisitableAstNode> lowTrueStatements = new() {
            _astBuilder.Assign(DataType.UINT8, al, new BinaryOperationNode(DataType.UINT8, al, adjustOp, _astBuilder.Constant.ToNode((byte)6)))
        };
        if (isSubtract) {
            lowTrueStatements.Add(_astBuilder.Assign(DataType.BOOL, finalCarryDeclaration.Reference,
                new BinaryOperationNode(DataType.BOOL, _astBuilder.Flag.Carry(), BinaryOperation.LOGICAL_OR, initialCfDeclaration.Reference)));
        }
        lowTrueStatements.Add(_astBuilder.Assign(DataType.BOOL, finalAuxDeclaration.Reference, _astBuilder.Constant.ToNode(true)));
        BlockNode adjustLowTrueCase = new BlockNode(lowTrueStatements.ToArray());
        IfElseNode adjustLowIf = _astBuilder.ControlFlow.If(adjustLowCondition, adjustLowTrueCase);
        ValueNode adjustCarryCondition = new BinaryOperationNode(
            DataType.BOOL,
            new BinaryOperationNode(DataType.BOOL, initialAlDeclaration.Reference, BinaryOperation.GREATER_THAN, _astBuilder.Constant.ToNode((byte)0x99)),
            BinaryOperation.LOGICAL_OR,
            initialCfDeclaration.Reference);
        BlockNode adjustCarryTrueCase = new BlockNode(
            _astBuilder.Assign(DataType.UINT8, al, new BinaryOperationNode(DataType.UINT8, al, adjustOp, _astBuilder.Constant.ToNode((byte)0x60))),
            _astBuilder.Assign(DataType.BOOL, finalCarryDeclaration.Reference, _astBuilder.Constant.ToNode(true)));
        BlockNode adjustCarryFalseCase = isSubtract
            ? new BlockNode()
            : new BlockNode(_astBuilder.Assign(DataType.BOOL, finalCarryDeclaration.Reference, _astBuilder.Constant.ToNode(false)));
        IfElseNode adjustCarryIf = new IfElseNode(adjustCarryCondition, adjustCarryTrueCase, adjustCarryFalseCase);
        MethodCallNode updateFlags = new MethodCallNode("Alu8", "UpdateFlags", al);
        InstructionNode displayAstNode = new InstructionNode(displayOp);
        List<IVisitableAstNode> execStatements = new() {
            initialAlDeclaration,
            initialCfDeclaration,
            finalAuxDeclaration,
            finalCarryDeclaration
        };
        if (isSubtract) {
            execStatements.Add(_astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Carry(), _astBuilder.Constant.ToNode(false)));
        }
        execStatements.Add(adjustLowIf);
        execStatements.Add(adjustCarryIf);
        execStatements.Add(updateFlags);
        execStatements.Add(_astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Auxiliary(), finalAuxDeclaration.Reference));
        execStatements.Add(_astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Carry(), finalCarryDeclaration.Reference));
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, execStatements.ToArray());
        instr.AttachAsts(displayAstNode, execAst);
        return instr;
    }

    public CfgInstruction ParseAsciiAdjust(ParsingContext context, BinaryOperation adjustOp, InstructionOperation displayOp) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, 1);
        ValueNode al = _astBuilder.Register.Reg8(RegisterIndex.AxIndex);
        ValueNode ax = _astBuilder.Register.Reg16(RegisterIndex.AxIndex);
        VariableDeclarationNode finalAuxDeclaration = _astBuilder.DeclareVariable(DataType.BOOL, "finalAuxiliaryFlag", _astBuilder.Constant.ToNode(false));
        VariableDeclarationNode finalCarryDeclaration = _astBuilder.DeclareVariable(DataType.BOOL, "finalCarryFlag", _astBuilder.Constant.ToNode(false));
        ValueNode lowNibble = new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.BITWISE_AND, _astBuilder.Constant.ToNode((byte)0x0F));
        ValueNode lowNibbleGreaterThan9 = new BinaryOperationNode(DataType.BOOL, lowNibble, BinaryOperation.GREATER_THAN, _astBuilder.Constant.ToNode((byte)9));
        ValueNode adjustCondition = new BinaryOperationNode(DataType.BOOL, lowNibbleGreaterThan9, BinaryOperation.LOGICAL_OR, _astBuilder.Flag.Auxiliary());
        List<IVisitableAstNode> adjustStatements = new();
        if (adjustOp == BinaryOperation.PLUS) {
            adjustStatements.Add(_astBuilder.Assign(DataType.UINT16, ax,
                new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.PLUS, _astBuilder.Constant.ToNode((ushort)0x0106))));
        } else {
            adjustStatements.Add(_astBuilder.Assign(DataType.UINT16, ax,
                new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.MINUS, _astBuilder.Constant.ToNode((ushort)6))));
            ValueNode ah = _astBuilder.Register.Reg8H(RegisterIndex.AxIndex);
            adjustStatements.Add(_astBuilder.Assign(DataType.UINT8, ah,
                new BinaryOperationNode(DataType.UINT8, ah, BinaryOperation.MINUS, _astBuilder.Constant.ToNode((byte)1))));
        }
        adjustStatements.Add(_astBuilder.Assign(DataType.BOOL, finalAuxDeclaration.Reference, _astBuilder.Constant.ToNode(true)));
        adjustStatements.Add(_astBuilder.Assign(DataType.BOOL, finalCarryDeclaration.Reference, _astBuilder.Constant.ToNode(true)));
        BlockNode adjustTrueCase = new BlockNode(adjustStatements.ToArray());
        IfElseNode adjustIf = _astBuilder.ControlFlow.If(adjustCondition, adjustTrueCase);
        MethodCallNode updateFlags = new MethodCallNode("Alu8", "UpdateFlags", al);
        InstructionNode displayAstNode = new InstructionNode(displayOp);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(
            instr,
            finalAuxDeclaration,
            finalCarryDeclaration,
            adjustIf,
            _astBuilder.Assign(DataType.UINT8, al, new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.BITWISE_AND, _astBuilder.Constant.ToNode((byte)0x0F))),
            updateFlags,
            _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Auxiliary(), finalAuxDeclaration.Reference),
            _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Carry(), finalCarryDeclaration.Reference));
        instr.AttachAsts(displayAstNode, execAst);
        return instr;
    }

    public CfgInstruction ParseAam(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, 1);
        InstructionField<byte> valueField = _instructionReader.UInt8.NextField(false);
        instr.AddField(valueField);
        ValueNode valueNode = _astBuilder.InstructionField.ToNode(valueField);
        ValueNode al = _astBuilder.Register.Reg8(RegisterIndex.AxIndex);
        ValueNode ah = _astBuilder.Register.Reg8H(RegisterIndex.AxIndex);
        VariableDeclarationNode valueDeclaration = _astBuilder.DeclareVariable(DataType.UINT8, "v2", valueNode);
        VariableDeclarationNode alDeclaration = _astBuilder.DeclareVariable(DataType.UINT8, "v1", al);
        VariableDeclarationNode resultDeclaration = _astBuilder.DeclareVariable(
            DataType.UINT8,
            "result",
            new BinaryOperationNode(DataType.UINT8, alDeclaration.Reference, BinaryOperation.MODULO, valueDeclaration.Reference));
        MethodCallNode updateFlags = new MethodCallNode("Alu8", "UpdateFlags", resultDeclaration.Reference);
        ValueNode isZeroCondition = new BinaryOperationNode(DataType.BOOL, valueDeclaration.Reference, BinaryOperation.EQUAL, _astBuilder.Constant.ToNode((byte)0));
        IfElseNode divisionCheck = _astBuilder.ControlFlow.ThrowIf<CpuDivisionErrorException>(isZeroCondition, "Division by zero");
        InstructionNode displayAst = new InstructionNode(InstructionOperation.AAM);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(
            instr,
            valueDeclaration,
            alDeclaration,
            divisionCheck,
            resultDeclaration,
            _astBuilder.Assign(DataType.UINT8, ah, new BinaryOperationNode(DataType.UINT8, alDeclaration.Reference, BinaryOperation.DIVIDE, valueDeclaration.Reference)),
            _astBuilder.Assign(DataType.UINT8, al, resultDeclaration.Reference),
            updateFlags);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseAad(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, 1);
        InstructionField<byte> valueField = _instructionReader.UInt8.NextField(false);
        instr.AddField(valueField);
        ValueNode valueNode = _astBuilder.InstructionField.ToNode(valueField);
        ValueNode al = _astBuilder.Register.Reg8(RegisterIndex.AxIndex);
        ValueNode ah = _astBuilder.Register.Reg8H(RegisterIndex.AxIndex);
        VariableDeclarationNode valueDeclaration = _astBuilder.DeclareVariable(DataType.UINT8, "v2", valueNode);
        ValueNode mul = new BinaryOperationNode(DataType.UINT8, ah, BinaryOperation.MULTIPLY, valueDeclaration.Reference);
        ValueNode sum = new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.PLUS, mul);
        MethodCallNode updateFlags = new MethodCallNode("Alu8", "UpdateFlags", al);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.AAD);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(
            instr,
            valueDeclaration,
            _astBuilder.Assign(DataType.UINT8, al, sum),
            _astBuilder.Assign(DataType.UINT8, ah, _astBuilder.Constant.ToNode((byte)0)),
            updateFlags,
            _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Carry(), _astBuilder.Constant.ToNode(false)),
            _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Auxiliary(), _astBuilder.Constant.ToNode(false)),
            _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Overflow(), _astBuilder.Constant.ToNode(false)));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
