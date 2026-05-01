namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>SAHF, LAHF, SALC</summary>
public class FlagTransferParser : BaseInstructionParser {
    public FlagTransferParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseSahf(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, 1);
        ValueNode ah = _astBuilder.Register.Reg8H(RegisterIndex.AxIndex);
        ValueNode flags32 = _astBuilder.Flag.FlagsRegister(DataType.UINT32);
        ValueNode preservedUpperFlags = new BinaryOperationNode(DataType.UINT32, flags32,
            BinaryOperation.BITWISE_AND, _astBuilder.Constant.ToNode(0xFFFFFF00u));
        ValueNode ahAsUInt32 = _astBuilder.TypeConversion.Convert(DataType.UINT32, ah);
        ValueNode mergedFlags32 = new BinaryOperationNode(DataType.UINT32, preservedUpperFlags,
            BinaryOperation.BITWISE_OR, ahAsUInt32);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.SAHF);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            _astBuilder.Assign(DataType.UINT32, flags32, mergedFlags32));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseLahf(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, 1);
        ValueNode ah = _astBuilder.Register.Reg8H(RegisterIndex.AxIndex);
        ValueNode flags = _astBuilder.Flag.FlagsRegister(DataType.UINT16);
        ValueNode flagsAsByte = _astBuilder.TypeConversion.Convert(DataType.UINT8, flags);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.LAHF);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            _astBuilder.Assign(DataType.UINT8, ah, flagsAsByte));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseSalc(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, 1);
        ValueNode al = _astBuilder.Register.Reg8(RegisterIndex.AxIndex);
        IfElseNode ternaryAssign = _astBuilder.ControlFlow.TernaryAssign(DataType.UINT8, al,
            _astBuilder.Flag.Carry(),
            _astBuilder.Constant.ToNode((byte)0xFF),
            _astBuilder.Constant.ToNode((byte)0));
        InstructionNode displayAst = new InstructionNode(InstructionOperation.SALC);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, ternaryAssign);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
