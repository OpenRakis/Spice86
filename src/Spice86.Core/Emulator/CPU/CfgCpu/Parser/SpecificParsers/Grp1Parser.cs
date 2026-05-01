namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for GRP1 instructions (opcodes 80-83): ALU operations with ModRM + immediate.
/// 8 operations: Add, Or, Adc, Sbb, And, Sub, Xor, Cmp.
/// </summary>
public class Grp1Parser : BaseGrpOperationParser {
    private static readonly (string Operation, InstructionOperation DisplayOp, bool Assign)[] Operations =
        AluOperationTable.Operations;

    public Grp1Parser(ParsingTools parsingTools) : base(parsingTools) {
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        if (groupIndex > 7) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }
        (string operation, InstructionOperation displayOp, bool assign) = Operations[groupIndex];
        ushort opCode = context.OpcodeField.Value;
        bool signExtendOp2 = opCode is 0x83;
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode displayImmNode;
        ValueNode execImmNode;
        if (signExtendOp2) {
            ValueNode rawNode = ReadSignedImmediate(instr, BitWidth.BYTE_8);
            displayImmNode = _astBuilder.TypeConversion.Convert(_astBuilder.SType(bitWidth), rawNode);
            execImmNode = _astBuilder.SignExtendToUnsigned(rawNode, BitWidth.BYTE_8, bitWidth);
        } else {
            displayImmNode = execImmNode = ReadUnsignedImmediate(instr, bitWidth);
        }
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        MethodCallValueNode aluCall = _astBuilder.AluCall(dataType, bitWidth, operation, rmNode, execImmNode);
        InstructionNode displayAst = new InstructionNode(displayOp, rmNode, displayImmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            _astBuilder.ConditionalAssign(dataType, rmNode, aluCall, assign));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }


}
