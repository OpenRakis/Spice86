namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// Abstract base for AST nodes that reference a <see cref="CfgInstruction"/>.
/// Equality is based on <see cref="InstructionId"/> and the concrete subclass type
/// so that the same instruction identity produces equal AST nodes.
/// </summary>
public abstract class CfgInstructionNode : IVisitableAstNode {
    protected CfgInstructionNode(CfgInstruction instruction) {
        Instruction = instruction;
        InstructionId = instruction.Id;
    }

    /// <summary>
    /// Stable identifier captured at construction time from <see cref="CfgInstruction.Id"/>.
    /// Used for fingerprinting without capturing the mutable <see cref="CfgInstruction"/> reference.
    /// </summary>
    public int InstructionId { get; }

    public CfgInstruction Instruction { get; }

    public abstract T Accept<T>(IAstVisitor<T> visitor);
}