namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parses memory bytes into ALU operation instructions.
/// The operation is determined by bits 5-3 of the opcode (octal operation index).
/// For each ALU operation, there are 6 instruction variants (bits 2-0):
///  - 000: RM &lt;- REG, 8-bit
///  - 001: RM &lt;- REG, 16/32-bit
///  - 010: REG &lt;- RM, 8-bit
///  - 011: REG &lt;- RM, 16/32-bit
///  - 100: ACC &lt;- IMM, 8-bit
///  - 101: ACC &lt;- IMM, 16/32-bit
/// </summary>
public class AluOperationParser : BaseInstructionParser {
    private const byte ModRmMask = 0b100;
    private const byte RmRegDirectionMask = 0b10;

    /// <summary>
    /// ALU operations indexed by bits 5-3 of the opcode.
    /// Same order as GRP1 ModRM.reg field (opcodes 0x80-0x83).
    /// </summary>
    private static readonly (string Operation, InstructionOperation DisplayOp, bool Assign)[] AluOperations =
        AluOperationTable.Operations;

    public AluOperationParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, int operationIndex) {
        (string aluOperation, InstructionOperation displayOp, bool assign) = AluOperations[operationIndex];
        ushort opcode = context.OpcodeField.Value;
        bool hasModRm = (opcode & ModRmMask) == 0;
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);

        ValueNode destNode;
        ValueNode srcNode;
        if (hasModRm) {
            ModRmContext modRmContext = _modRmParser.ParseNext(context);
            RegisterModRmFields(instr, modRmContext);
            bool rmIsDestination = (opcode & RmRegDirectionMask) == 0;
            if (rmIsDestination) {
                destNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
                srcNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
            } else {
                destNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
                srcNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
            }
        } else {
            srcNode = ReadUnsignedImmediate(instr, bitWidth);
            destNode = _astBuilder.Register.Accumulator(dataType);
        }

        return BuildAluInstruction(instr, aluOperation, displayOp, assign, dataType, bitWidth, destNode, srcNode);
    }

    private CfgInstruction BuildAluInstruction(CfgInstruction instr, string aluOperation,
        InstructionOperation displayOp, bool assign, DataType dataType, BitWidth bitWidth,
        ValueNode destNode, ValueNode srcNode) {
        MethodCallValueNode aluCall = _astBuilder.AluCall(dataType, bitWidth, aluOperation, destNode, srcNode);
        InstructionNode displayAst = new InstructionNode(displayOp, destNode, srcNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            _astBuilder.ConditionalAssign(dataType, destNode, aluCall, assign));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}