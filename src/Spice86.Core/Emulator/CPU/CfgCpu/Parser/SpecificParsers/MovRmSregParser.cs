namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>MOV RM, SReg (always 16-bit)</summary>
public class MovRmSregParser : OperationModRmParser {
    public MovRmSregParser(ParsingTools parsingTools) : base(parsingTools, false) {
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        if (modRmContext.RegisterIndex > (int)SegmentRegisterIndex.GsIndex) {
            throw new CpuInvalidOpcodeException($"Invalid segment register index {modRmContext.RegisterIndex} for MOV r/m,Sreg");
        }
        DataType destinationType = modRmContext.Mode == 3 ? dataType : DataType.UINT16;
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(destinationType, modRmContext);
        ValueNode sregNode = _astBuilder.Register.SReg(modRmContext.RegisterIndex);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.MOV, rmNode, sregNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(destinationType, rmNode, sregNode));
        instr.AttachAsts(displayAst, execAst);
    }
}
