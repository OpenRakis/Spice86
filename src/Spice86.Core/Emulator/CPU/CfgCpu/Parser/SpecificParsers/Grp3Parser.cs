namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for GRP3 instructions (opcodes F6/F7):
/// Test, Not, Neg, Mul, Imul, Div, Idiv.
/// </summary>
public class Grp3Parser : BaseGrpOperationParser {
    public Grp3Parser(ParsingTools parsingTools) : base(parsingTools) {
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        return groupIndex switch {
            0 => ParseTest(context, modRmContext, bitWidth, dataType),
            2 => ParseNot(context, modRmContext, bitWidth, dataType),
            3 => ParseNeg(context, modRmContext, bitWidth, dataType),
            4 => ParseMulImul(context, modRmContext, bitWidth, dataType, "Mul", InstructionOperation.MUL, false),
            5 => ParseMulImul(context, modRmContext, bitWidth, dataType, "Imul", InstructionOperation.IMUL, true),
            6 => ParseDiv(context, modRmContext, bitWidth, dataType, "Div", InstructionOperation.DIV, false),
            7 => ParseDiv(context, modRmContext, bitWidth, dataType, "Idiv", InstructionOperation.IDIV, true),
            _ => throw new InvalidGroupIndexException(_state, groupIndex)
        };
    }

    private CfgInstruction ParseTest(ParsingContext context, ModRmContext modRmContext,
        BitWidth bitWidth, DataType dataType) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode immNode = ReadUnsignedImmediate(instr, bitWidth);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        MethodCallValueNode andCall = _astBuilder.AluCall(dataType, bitWidth, "And", rmNode, immNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.TEST, rmNode, immNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, andCall);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParseNot(ParsingContext context, ModRmContext modRmContext,
        BitWidth bitWidth, DataType dataType) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        UnaryOperationNode notOp = new UnaryOperationNode(dataType, UnaryOperation.BITWISE_NOT, rmNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.NOT, rmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            _astBuilder.Assign(dataType, rmNode, notOp));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParseNeg(ParsingContext context, ModRmContext modRmContext,
        BitWidth bitWidth, DataType dataType) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        ValueNode zeroNode = _astBuilder.Constant.ToNode(dataType, 0UL);
        MethodCallValueNode subCall = _astBuilder.AluCall(dataType, bitWidth, "Sub", zeroNode, rmNode);
        BinaryOperationNode assignment = _astBuilder.Assign(dataType, rmNode, subCall);
        BinaryOperationNode notEqualsZero = new BinaryOperationNode(DataType.BOOL,
            rmNode, BinaryOperation.NOT_EQUAL, zeroNode);
        BinaryOperationNode setCarry = _astBuilder.Assign(DataType.BOOL,
            _astBuilder.Flag.Carry(), notEqualsZero);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.NEG, rmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, assignment, setCarry);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParseMulImul(ParsingContext context, ModRmContext modRmContext,
        BitWidth bitWidth, DataType dataType, string operation, InstructionOperation displayOp, bool isSigned) {
        DataType unsignedType = _astBuilder.UType(bitWidth);
        DataType signedType = isSigned ? _astBuilder.SType(bitWidth) : _astBuilder.UType(bitWidth);
        DataType wideType = isSigned ? _astBuilder.SType(bitWidth.Double()) : _astBuilder.UType(bitWidth.Double());
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        (ValueNode sourceReg, ValueNode additionalReg) = GetMulDivRegisterNodes(bitWidth);
        ValueNode v1 = _astBuilder.TypeConversion.Convert(signedType, sourceReg);
        ValueNode v2 = _astBuilder.ModRm.RmToNodeSigned(unsignedType, modRmContext);
        VariableDeclarationNode resultDecl = _astBuilder.DeclareAluResult(wideType, bitWidth, operation, "result", v1, v2);
        ValueNode upperPart = _astBuilder.ExtractUpperBits(resultDecl.Reference, bitWidth, unsignedType);
        IVisitableAstNode assignUpper = _astBuilder.AssignWithConversion(unsignedType, additionalReg, upperPart);
        ValueNode lowerPart = _astBuilder.ExtractLowerBits(resultDecl.Reference, unsignedType);
        IVisitableAstNode assignLower = _astBuilder.AssignWithConversion(unsignedType, sourceReg, lowerPart);
        InstructionNode displayAst = new InstructionNode(displayOp,
            _astBuilder.ModRm.RmToNode(unsignedType, modRmContext));
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, resultDecl, assignUpper, assignLower);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParseDiv(ParsingContext context, ModRmContext modRmContext,
        BitWidth bitWidth, DataType dataType, string operation, InstructionOperation displayOp, bool isSigned) {
        DataType unsignedType = _astBuilder.UType(bitWidth);
        DataType signedType = isSigned ? _astBuilder.SType(bitWidth) : _astBuilder.UType(bitWidth);
        DataType wideSignedType = isSigned ? _astBuilder.SType(bitWidth.Double()) : _astBuilder.UType(bitWidth.Double());
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        (ValueNode lowReg, ValueNode highReg) = GetMulDivRegisterNodes(bitWidth);
        ValueNode v1Node;
        if (bitWidth == BitWidth.BYTE_8) {
            v1Node = _astBuilder.TypeConversion.Convert(wideSignedType,
                _astBuilder.Register.Reg16(RegisterIndex.AxIndex));
        } else {
            v1Node = _astBuilder.CombineHighLowRegisters(highReg, lowReg, bitWidth, wideSignedType);
        }
        ValueNode divisorExpr = _astBuilder.TypeConversion.Convert(signedType,
            _astBuilder.ModRm.RmToNode(unsignedType, modRmContext));
        VariableDeclarationNode divisorNode = _astBuilder.DeclareVariable(signedType, "divisor", divisorExpr);
        VariableDeclarationNode dividendNode = _astBuilder.DeclareVariable(wideSignedType, "dividend", v1Node);
        VariableDeclarationNode quotientDecl = _astBuilder.DeclareAluResult(signedType, bitWidth, operation,
            "quotient", dividendNode.Reference, divisorNode.Reference);
        IVisitableAstNode assignQuotient = _astBuilder.AssignWithConversion(unsignedType, lowReg, quotientDecl.Reference);
        BinaryOperationNode moduloOp = new BinaryOperationNode(wideSignedType,
            dividendNode.Reference, BinaryOperation.MODULO,
            _astBuilder.TypeConversion.Convert(wideSignedType, divisorNode.Reference));
        ValueNode remainderNarrowed = _astBuilder.TypeConversion.Convert(unsignedType, moduloOp);
        IVisitableAstNode assignRemainder = _astBuilder.AssignWithConversion(unsignedType, highReg, remainderNarrowed);
        InstructionNode displayAst = new InstructionNode(displayOp,
            _astBuilder.ModRm.RmToNode(unsignedType, modRmContext));
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            divisorNode, dividendNode, quotientDecl, assignQuotient, assignRemainder);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private (ValueNode SourceOrLow, ValueNode AdditionalOrHigh) GetMulDivRegisterNodes(BitWidth bitWidth) {
        return bitWidth switch {
            BitWidth.BYTE_8 => (_astBuilder.Register.Reg8L(RegisterIndex.AxIndex), _astBuilder.Register.Reg8H(RegisterIndex.AxIndex)),
            BitWidth.WORD_16 => (_astBuilder.Register.Reg16(RegisterIndex.AxIndex), _astBuilder.Register.Reg16(RegisterIndex.DxIndex)),
            BitWidth.DWORD_32 => (_astBuilder.Register.Reg32(RegisterIndex.AxIndex), _astBuilder.Register.Reg32(RegisterIndex.DxIndex)),
            _ => throw CreateUnsupportedBitWidthException(bitWidth)
        };
    }


}
