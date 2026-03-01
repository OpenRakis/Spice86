namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

public record InstructionNode(RepPrefix? RepPrefix, InstructionOperation Operation, params ValueNode[] Parameters) : IVisitableAstNode {
    public InstructionNode(InstructionOperation operation, params ValueNode[] parameters) : this(null, operation, parameters) {
    }

    public virtual T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitInstructionNode(this);
    }
}