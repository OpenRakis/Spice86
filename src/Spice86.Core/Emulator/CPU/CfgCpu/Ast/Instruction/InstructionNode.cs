namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

public class InstructionNode(RepPrefix? repPrefix, InstructionOperation operation, params ValueNode[] parameters) : IVisitableAstNode {
    public InstructionNode(InstructionOperation operation, params ValueNode[] parameters) : this(null, operation, parameters) {
    }
    public RepPrefix? RepPrefix { get; } = repPrefix;
    public InstructionOperation Operation { get; } = operation;
    public IReadOnlyList<ValueNode> Parameters { get; } = parameters;
    
    public virtual T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitInstructionNode(this);
    }
}