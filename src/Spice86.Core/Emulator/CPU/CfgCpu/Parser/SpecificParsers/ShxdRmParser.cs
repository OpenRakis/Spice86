namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>SHLD/SHRD RM, R, IMM8 or CL (double-precision shift)</summary>
public class ShxdRmParser : BaseInstructionParser {
    public ShxdRmParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    /// <summary>
    /// Parses a double-precision shift instruction.
    /// </summary>
    public CfgInstruction Parse(ParsingContext context, string operation, InstructionOperation displayOp, bool useImm8) {
        (CfgInstruction instr, DataType dataType, _, ModRmContext modRmContext) = ParseModRm(context, false, 1);
        (ValueNode rNode, ValueNode rmNode) = _astBuilder.ModRmOperands(dataType, modRmContext);
        ValueNode countNode;
        if (useImm8) {
            InstructionField<byte> immField = _instructionReader.UInt8.NextField(false);
            instr.AddField(immField);
            countNode = _astBuilder.InstructionField.ToNode(immField);
        } else {
            countNode = _astBuilder.Register.Reg8(RegisterIndex.CxIndex);
        }
        MethodCallValueNode shiftCall = _astBuilder.AluCall(dataType, dataType.BitWidth, operation, rmNode, rNode, countNode);
        InstructionNode displayAst = new InstructionNode(displayOp, rmNode, rNode, countNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(dataType, rmNode, shiftCall));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
