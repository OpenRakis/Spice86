namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using System.Threading;

public abstract class CfgNode : ICfgNode {
    public CfgNode(int id, SegmentedAddress address, int? maxSuccessorsCount) {
        Id = id;
        Address = address;
        // Assign the backing field directly: the virtual MaxSuccessorsCount setter is overridden
        // by CfgBlock to throw NotSupportedException, so going through the property here would
        // make CfgBlock impossible to construct. Subclasses that override the property are
        // responsible for ignoring or surfacing this initial value as appropriate.
        _maxSuccessorsCount = maxSuccessorsCount;
        _compiledExecution = CreateUninitializedCompiledExecution(address, Id);
    }

    public int Id { get; }
    public virtual HashSet<ICfgNode> Predecessors { get; } = new();
    public virtual HashSet<ICfgNode> Successors { get; } = new();
    public SegmentedAddress Address { get; }
    public virtual bool CanCauseContextRestore => false;

    private long _compilationGeneration;

    /// <inheritdoc />
    public long CompilationGeneration => Interlocked.Read(ref _compilationGeneration);

    /// <inheritdoc />
    public long IncrementCompilationGeneration() => Interlocked.Increment(ref _compilationGeneration);

    private volatile CfgNodeExecutionAction<InstructionExecutionHelper> _compiledExecution;

    public CfgNodeExecutionAction<InstructionExecutionHelper> CompiledExecution {
        get => _compiledExecution;
        set => _compiledExecution = value;
    }

    public abstract bool IsLive { get; }
    
    public abstract void UpdateSuccessorCache();
    public abstract ICfgNode? GetNextSuccessor(InstructionExecutionHelper helper);

    /// <summary>
    /// Pre-built Abstract Syntax Tree representing the grammar of the assembly instruction (for display).
    /// </summary>
    public abstract IVisitableAstNode DisplayAst { get; }

    /// <summary>
    /// Pre-built Abstract Syntax Tree representing the execution logic of this node.
    /// </summary>
    public abstract IVisitableAstNode ExecutionAst { get; }

    private int? _maxSuccessorsCount;
    public virtual int? MaxSuccessorsCount {
        get => _maxSuccessorsCount;
        set => _maxSuccessorsCount = value;
    }

    public virtual bool CanHaveMoreSuccessors { get; set; } = true;
    
    public virtual ICfgNode? UniqueSuccessor { get; set; }

    /// <summary>
    /// Default to <c>false</c>. <see cref="ParsedInstruction.CfgInstruction"/> and
    /// <see cref="ParsedInstruction.SelfModifying.SelectorNode"/> override this to provide their own logic.
    /// </summary>
    public virtual bool IsBlockTerminator => false;

    /// <summary>
    /// Default to <c>false</c>. <see cref="ParsedInstruction.CfgInstruction"/> overrides this to expose its
    /// stored explicit starter flag.
    /// </summary>
    public virtual bool IsBlockStarter => false;

    /// <summary>
    /// Containing-<see cref="CfgBlock"/> back-pointer. Defaults to <c>null</c>.
    /// <see cref="ParsedInstruction.CfgInstruction"/> and
    /// <see cref="ParsedInstruction.SelfModifying.SelectorNode"/> override this with a settable
    /// auto-property back-pointer maintained by <see cref="Linker.NodeLinker"/>.
    /// <see cref="CfgBlock"/> keeps the default <c>null</c> because a block is itself the container,
    /// not contained.
    /// <see cref="Linker.NodeLinker"/> is the sole writer of this property.
    /// </summary>
    public virtual CfgBlock? ContainingBlock { get; set; }

    private static CfgNodeExecutionAction<InstructionExecutionHelper> CreateUninitializedCompiledExecution(
        SegmentedAddress address,
        int id) {
        return helper => throw new InvalidOperationException($"CompiledExecution was not initialized for node at {address} with id {id}");
    }

    public override string ToString() {
        return $"CfgNode of type {GetType()} with address {Address} and id {Id} IsLive {IsLive}";
    }
}