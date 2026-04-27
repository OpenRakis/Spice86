namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>MOV SReg, RM16</summary>
public class MovSregRm16Parser : BaseInstructionParser {
    public MovSregRm16Parser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        if (modRmContext.RegisterIndex == (uint)SegmentRegisterIndex.CsIndex) {
            throw new CpuInvalidOpcodeException("Attempted to write to CS register with MOV instruction");
        }
        ValueNode sregNode = _astBuilder.Register.SReg(modRmContext.RegisterIndex);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        BinaryOperationNode assignment = _astBuilder.Assign(DataType.UINT16, sregNode, rmNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.MOV, sregNode, rmNode);
        IVisitableAstNode execAst;
        if (modRmContext.RegisterIndex == (uint)SegmentRegisterIndex.SsIndex) {
            IVisitableAstNode setInterruptShadowing = _astBuilder.Flag.SetInterruptShadowing();
            execAst = _astBuilder.WithIpAdvancement(instr, assignment, setInterruptShadowing);
        } else {
            execAst = _astBuilder.WithIpAdvancement(instr, assignment);
        }
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
