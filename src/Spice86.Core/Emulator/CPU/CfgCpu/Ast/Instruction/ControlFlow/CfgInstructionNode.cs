namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// Abstract base for AST nodes that reference a <see cref="CfgInstruction"/>.
/// </summary>
public abstract class CfgInstructionNode(CfgInstruction instruction) : IVisitableAstNode {
    public CfgInstruction Instruction { get; } = instruction;

    public abstract T Accept<T>(IAstVisitor<T> visitor);
}