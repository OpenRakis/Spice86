namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public abstract class CfgNode : ICfgNode {
    private static int _nextId;
    public CfgNode(SegmentedAddress address, int? maxSuccessorsCount) {
        Address = address;
        Id = _nextId++;
        MaxSuccessorsCount = maxSuccessorsCount;
    }

    public int Id { get; }
    public HashSet<ICfgNode> Predecessors { get; } = new();
    public HashSet<ICfgNode> Successors { get; } = new();
    public SegmentedAddress Address { get; }
    public virtual bool CanCauseContextRestore => false;
   
    public abstract bool IsLive { get; }
    
    public abstract void UpdateSuccessorCache();

    public abstract void Execute(InstructionExecutionHelper helper);

    public abstract InstructionNode ToInstructionAst(AstBuilder builder);

    public abstract IVisitableAstNode GetExecutionAst(AstBuilder builder);

    public int? MaxSuccessorsCount { get; set; }

    public bool CanHaveMoreSuccessors { get; set; } = true;
    
    public ICfgNode? UniqueSuccessor { get; set; }

    public override string ToString() {
        return $"CfgNode of type {GetType()} with address {Address} and id {Id} IsLive {IsLive}";
    }
}