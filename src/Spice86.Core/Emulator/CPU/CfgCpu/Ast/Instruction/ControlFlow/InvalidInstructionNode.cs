namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;

public class InvalidInstructionNode : CfgInstructionNode {
    public InvalidInstructionNode(CfgInstruction instruction, CpuException cpuException) : base(instruction) {
        CpuException = cpuException;
    }

    public CpuException CpuException { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitInvalidInstructionNode(this);
    }
}