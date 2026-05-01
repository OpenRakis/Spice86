namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>INC/DEC/PUSH/POP/XCHG register</summary>
public class RegisterOpParser : BaseInstructionParser {
    public RegisterOpParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseIncDecReg(ParsingContext context, int regIndex, string aluOperation, InstructionOperation displayOp) {
        BitWidth bitWidth = context.DefaultWordOperandBitWidth;
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode regNode = _astBuilder.Register.Reg(dataType, regIndex);
        MethodCallValueNode aluCall = _astBuilder.AluCall(dataType, bitWidth, aluOperation, regNode);
        InstructionNode displayAst = new InstructionNode(displayOp, regNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(dataType, regNode, aluCall));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParsePushReg(ParsingContext context, int regIndex) {
        BitWidth bitWidth = context.DefaultWordOperandBitWidth;
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode regNode = _astBuilder.Register.Reg(dataType, regIndex);
        MethodCallNode pushBlock = _astBuilder.Stack.Push(dataType, regNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.PUSH, regNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, pushBlock);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParsePopReg(ParsingContext context, int regIndex) {
        BitWidth bitWidth = context.DefaultWordOperandBitWidth;
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode regNode = _astBuilder.Register.Reg(dataType, regIndex);
        ValueNode popValue = _astBuilder.Stack.Pop(bitWidth);
        BinaryOperationNode assign = new BinaryOperationNode(dataType, regNode, BinaryOperation.ASSIGN, popValue);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.POP, regNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, assign);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseXchgRegAcc(ParsingContext context, int regIndex) {
        BitWidth bitWidth = context.DefaultWordOperandBitWidth;
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode regNode = _astBuilder.Register.Reg(dataType, regIndex);
        ValueNode accNode = _astBuilder.Register.Accumulator(dataType);
        VariableDeclarationNode tempDecl = _astBuilder.DeclareVariable(dataType, "temp", regNode);
        BinaryOperationNode assignReg = _astBuilder.Assign(dataType, regNode, accNode);
        BinaryOperationNode assignAcc = _astBuilder.Assign(dataType, accNode, tempDecl.Reference);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.XCHG, regNode, accNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, tempDecl, assignReg, assignAcc);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
