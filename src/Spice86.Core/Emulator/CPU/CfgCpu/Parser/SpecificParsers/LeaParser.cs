namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;

/// <summary>LEA R, RM (load effective address)</summary>
public class LeaParser : OperationModRmParser {
    public LeaParser(ParsingTools parsingTools) : base(parsingTools, false) {
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        // LEA with a register source operand (mod=11) is invalid; the source
        // must address memory so an effective address can be computed. Real
        // hardware raises #UD; the SingleStepTests test cases for this form
        // are tagged "(bad)" and expect EIP to remain on the LEA instruction.
        if (modRmContext.MemoryAddressType == MemoryAddressType.NONE) {
            throw new CpuInvalidOpcodeException("LEA with register source operand is invalid");
        }
        ValueNode rNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
        ValueNode offsetNode = _astBuilder.ModRm.MemoryOffsetToNode(modRmContext);
        ValueNode convertedOffset = _astBuilder.TypeConversion.Convert(dataType, offsetNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.LEA, rNode, _astBuilder.ModRm.MemoryOffsetToNode(modRmContext));
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(dataType, rNode, convertedOffset));
        instr.AttachAsts(displayAst, execAst);
    }
}
