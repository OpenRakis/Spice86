namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// MOV to/from a control register (0F 20 / 0F 22). The ModRM reg field selects the control register
/// number (only CR0/CR2/CR3/CR4 are valid); the mod field is ignored on real hardware and the r/m
/// field always names a general-purpose register.
/// </summary>
public class ControlRegisterParser : BaseInstructionParser {
    public ControlRegisterParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    /// <summary>MOV r32, CRn (0F 20): reads a control register into a general-purpose register.</summary>
    public CfgInstruction ParseMovRegFromCr(ParsingContext context) {
        return Parse(context, isLoad: true);
    }

    /// <summary>MOV CRn, r32 (0F 22): writes a general-purpose register into a control register.</summary>
    public CfgInstruction ParseMovCrFromReg(ParsingContext context) {
        return Parse(context, isLoad: false);
    }

    private CfgInstruction Parse(ParsingContext context, bool isLoad) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        int crNumber = modRmContext.RegisterIndex;
        if (crNumber is not (0 or 2 or 3 or 4)) {
            throw new CpuInvalidOpcodeException($"MOV to/from CR{crNumber} is not supported");
        }

        // The mod field is ignored for MOV to/from control registers: r/m always names a GP register.
        ValueNode gpRegNode = _astBuilder.Register.Reg32((RegisterIndex)modRmContext.RegisterMemoryIndex);
        ValueNode crNumberNode = _astBuilder.Constant.ToNode((uint)crNumber);
        InstructionNode displayAst;
        IVisitableAstNode execAst;
        if (isLoad) {
            MethodCallValueNode readCr = new MethodCallValueNode(DataType.UINT32, null,
                nameof(InstructionExecutionHelper.ReadControlRegister), crNumberNode);
            displayAst = new InstructionNode(InstructionOperation.MOV, gpRegNode, crNumberNode);
            execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(DataType.UINT32, gpRegNode, readCr));
        } else {
            MethodCallNode writeCr = new MethodCallNode(null,
                nameof(InstructionExecutionHelper.WriteControlRegister), crNumberNode, gpRegNode);
            displayAst = new InstructionNode(InstructionOperation.MOV, crNumberNode, gpRegNode);
            execAst = _astBuilder.WithIpAdvancement(instr, writeCr);
        }
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
