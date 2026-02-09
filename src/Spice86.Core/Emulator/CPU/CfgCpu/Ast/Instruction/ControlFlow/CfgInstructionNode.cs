namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public abstract class CfgInstructionNode : IVisitableAstNode {
    protected CfgInstructionNode(CfgInstruction instruction) {
        Instruction = instruction;
    }

    public CfgInstruction Instruction { get; }

    public abstract T Accept<T>(IAstVisitor<T> visitor);
}