namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

/// <summary>
/// MOV DRn, r/m32 (0F 23 /r): the ModRM reg field selects the debug register number and the Mod/RM
/// operand (register or memory) supplies the 32-bit value.
/// </summary>
public class DebugRegisterParser : BaseInstructionParser {
    public DebugRegisterParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        int drNumber = modRmContext.RegisterIndex;
        ValueNode sourceNode = _astBuilder.ModRm.RmToNode(DataType.UINT32, modRmContext);
        ValueNode drNumberNode = _astBuilder.Constant.ToNode((uint)drNumber);
        MethodCallNode writeDr = new InstructionExecutionHelperCallNode(
            InstructionExecutionHelperOperation.WriteDebugRegister, drNumberNode, sourceNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.MOV, drNumberNode, sourceNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, writeDr);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
