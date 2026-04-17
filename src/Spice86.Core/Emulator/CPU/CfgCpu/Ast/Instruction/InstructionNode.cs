namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public record InstructionNode(RepPrefix? RepPrefix, InstructionOperation Operation, params IVisitableAstNode[] Parameters) : IVisitableAstNode {
    public InstructionNode(InstructionOperation operation, params IVisitableAstNode[] parameters) : this(null, operation, parameters) {
    }

    public virtual T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitInstructionNode(this);
    }
    
}