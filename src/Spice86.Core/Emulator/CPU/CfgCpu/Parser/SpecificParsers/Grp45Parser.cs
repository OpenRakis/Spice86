namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for GRP4 (opcode FE) and GRP5 (opcode FF) instructions.
/// GRP4: INC RM8, DEC RM8, Callback.
/// GRP5: INC, DEC, CALL NEAR, CALL FAR, JMP NEAR, JMP FAR, PUSH RM.
/// </summary>
public class Grp45Parser : BaseGrpOperationParser {
    public Grp45Parser(ParsingTools parsingTools) : base(parsingTools) {
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        bool grp4 = context.OpcodeField.Value is 0xFE;
        if (grp4) {
            return ParseGrp4(context, modRmContext, groupIndex);
        }
        return ParseGrp5(context, modRmContext, groupIndex);
    }

    private CfgInstruction ParseGrp4(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        return groupIndex switch {
            0 => ParseIncDec(context, modRmContext, BitWidth.BYTE_8, "Inc", InstructionOperation.INC),
            1 => ParseIncDec(context, modRmContext, BitWidth.BYTE_8, "Dec", InstructionOperation.DEC),
            7 => ParseCallback(context, modRmContext),
            _ => throw new InvalidGroupIndexException(_state, groupIndex)
        };
    }

    private CfgInstruction ParseGrp5(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        BitWidth bitWidth = context.DefaultWordOperandBitWidth;
        return groupIndex switch {
            0 => ParseIncDec(context, modRmContext, bitWidth, "Inc", InstructionOperation.INC),
            1 => ParseIncDec(context, modRmContext, bitWidth, "Dec", InstructionOperation.DEC),
            2 => ParseCallNear(context, modRmContext, bitWidth),
            3 => ParseCallFar(context, modRmContext, bitWidth),
            4 => ParseJumpNear(context, modRmContext),
            5 => ParseJumpFar(context, modRmContext),
            6 => ParsePush(context, modRmContext, bitWidth),
            _ => throw new InvalidGroupIndexException(_state, groupIndex)
        };
    }

    private CfgInstruction ParseIncDec(ParsingContext context, ModRmContext modRmContext,
        BitWidth bitWidth, string operation, InstructionOperation displayOp) {
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        MethodCallValueNode aluCall = _astBuilder.AluCall(dataType, bitWidth, operation, rmNode);
        InstructionNode displayAst = new InstructionNode(displayOp, rmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            _astBuilder.Assign(dataType, rmNode, aluCall));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParseCallback(ParsingContext context, ModRmContext modRmContext) {
        InstructionField<ushort> callbackNumber = _instructionReader.UInt16.NextField(true);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null);
        RegisterModRmFields(instr, modRmContext);
        instr.AddField(callbackNumber);
        ValueNode callbackNode = _astBuilder.InstructionField.ToNode(callbackNumber);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.CALLBACK, callbackNode);
        IVisitableAstNode execAst = new CallbackNode(instr, _astBuilder.Constant.ToNode(callbackNumber.Value));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParseCallNear(ParsingContext context, ModRmContext modRmContext, BitWidth operandBitWidth) {
        DataType dataType = DataType.UINT16;
        if (operandBitWidth == BitWidth.DWORD_32) {
            dataType = DataType.UINT32;
        }
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null);
        RegisterModRmFields(instr, modRmContext);
        ValueNode targetIp = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        if (operandBitWidth == BitWidth.DWORD_32) {
            targetIp = _astBuilder.TypeConversion.Convert(DataType.UINT16, targetIp);
        }
        InstructionNode displayAst = new InstructionNode(InstructionOperation.CALL_NEAR,
            _astBuilder.ModRm.RmToNode(dataType, modRmContext));
        IVisitableAstNode execAst = new CallNearNode(instr, targetIp, operandBitWidth);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParseCallFar(ParsingContext context, ModRmContext modRmContext, BitWidth operandBitWidth) {
        ModRmContext ensuredModRm = _modRmParser.EnsureNotMode3(modRmContext);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null);
        RegisterModRmFields(instr, ensuredModRm);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.CALL_FAR,
            _astBuilder.ModRm.ToMemoryAddressNode(DataType.UINT32, ensuredModRm));
        SegmentedAddressNode targetAddress = _astBuilder.ModRm.ToSegmentedAddressNode(operandBitWidth, ensuredModRm);
        IVisitableAstNode execAst = new CallFarNode(instr, targetAddress, operandBitWidth);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParseJumpNear(ParsingContext context, ModRmContext modRmContext) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null);
        RegisterModRmFields(instr, modRmContext);
        ValueNode targetIp = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.JMP_NEAR, targetIp);
        IVisitableAstNode execAst = new JumpNearNode(instr, targetIp);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParseJumpFar(ParsingContext context, ModRmContext modRmContext) {
        ModRmContext ensuredModRm = _modRmParser.EnsureNotMode3(modRmContext);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null);
        RegisterModRmFields(instr, ensuredModRm);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.JMP_FAR,
            _astBuilder.ModRm.ToMemoryAddressNode(DataType.UINT32, ensuredModRm));
        SegmentedAddressNode targetAddress = _astBuilder.ModRm.ToSegmentedAddressNode(BitWidth.WORD_16, ensuredModRm);
        IVisitableAstNode execAst = new JumpFarNode(instr, targetAddress);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction ParsePush(ParsingContext context, ModRmContext modRmContext, BitWidth bitWidth) {
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        MethodCallNode pushBlock = _astBuilder.Stack.Push(dataType, rmNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.PUSH, rmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, pushBlock);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
