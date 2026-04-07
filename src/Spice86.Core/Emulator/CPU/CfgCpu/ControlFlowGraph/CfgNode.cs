namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public abstract class CfgNode : ICfgNode {
    private static int _nextId;
    public CfgNode(SegmentedAddress address, int? maxSuccessorsCount) {
        Address = address;
        Id = _nextId++;
        MaxSuccessorsCount = maxSuccessorsCount;
        CompiledExecution = CreateUninitializedCompiledExecution(address, Id);
    }

    public int Id { get; }
    public HashSet<ICfgNode> Predecessors { get; } = new();
    public HashSet<ICfgNode> Successors { get; } = new();
    public SegmentedAddress Address { get; }
    public virtual bool CanCauseContextRestore => false;

    public CfgNodeExecutionAction<InstructionExecutionHelper> CompiledExecution { get; set; }
   
    public abstract bool IsLive { get; }
    
    public abstract void UpdateSuccessorCache();
    public abstract ICfgNode? GetNextSuccessor(InstructionExecutionHelper helper);

    public abstract InstructionNode ToInstructionAst(AstBuilder builder);

    private IVisitableAstNode? _cachedExecutionAst;

    /// <summary>
    /// Returns the cached execution AST, building it on first call.
    /// </summary>
    public IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
        return _cachedExecutionAst ??= BuildExecutionAst(builder);
    }

    /// <summary>
    /// Clears the cached execution AST so the next <see cref="GenerateExecutionAst"/> call rebuilds it.
    /// Thread-safe and idempotent.
    /// </summary>
    public void InvalidateExecutionAstCache() {
        Interlocked.Exchange(ref _cachedExecutionAst, null);
    }

    /// <summary>
    /// Template method for subclasses to build the execution AST.
    /// </summary>
    protected abstract IVisitableAstNode BuildExecutionAst(AstBuilder builder);

    public int? MaxSuccessorsCount { get; set; }

    public bool CanHaveMoreSuccessors { get; set; } = true;
    
    public ICfgNode? UniqueSuccessor { get; set; }

    private static CfgNodeExecutionAction<InstructionExecutionHelper> CreateUninitializedCompiledExecution(
        SegmentedAddress address,
        int id) {
        return helper => throw new InvalidOperationException($"CompiledExecution was not initialized for node at {address} with id {id}");
    }

    public override string ToString() {
        return $"CfgNode of type {GetType()} with address {Address} and id {Id} IsLive {IsLive}";
    }
}