namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Exceptions;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Linq;

/// <summary>
/// An <see cref="ICfgNode"/> that owns an ordered sequence of instruction nodes which always
/// execute together as a straight-line path: one entry, one exit, no intermediate join points.
/// Block-level successors/predecessors delegate by reference to the terminator/entry, so they
/// stay in lock-step with the instruction-level graph automatically.
/// </summary>
public sealed class CfgBlock : CfgNode {
    private readonly List<ICfgNode> _instructions = new();

    /// <summary>
    /// Number of contained nodes whose <see cref="ICfgNode.IsLive"/> is currently <c>false</c>.
    /// <see cref="IsLive"/> is <c>(_nonLiveCounter == 0)</c>.
    /// </summary>
    private int _nonLiveCounter;

    /// <summary>
    /// Number of contained nodes whose <see cref="ICfgNode.IsSpeculative"/> is currently <c>true</c>.
    /// <see cref="IsSpeculative"/> is <c>(_speculativeCounter &gt; 0)</c>.
    /// </summary>
    private int _speculativeCounter;

    private BlockNode? _cachedDisplayAst;
    private bool _isDisplayAstStale = true;

    public CfgBlock(int id, ICfgNode entry)
        : base(id, entry.Address, maxSuccessorsCount: null) {
        Append(entry);
    }

    public IReadOnlyList<ICfgNode> Instructions => _instructions;

    public ICfgNode Entry => _instructions[0];

    public ICfgNode Terminator => _instructions[_instructions.Count - 1];

    /// <summary>
    /// Whether the linker has finished discovering this block. Flipped to <c>true</c> once and
    /// never back.
    /// </summary>
    public bool IsDiscoveryComplete { get; internal set; }

    /// <inheritdoc />
    /// <remarks>
    /// O(1). The block is live iff every contained instruction is live; computed from the
    /// maintained <see cref="_nonLiveCounter"/> without iterating.
    /// </remarks>
    public override bool IsLive => _nonLiveCounter == 0;

    /// <inheritdoc />
    /// <remarks>
    /// O(1). The block is speculative iff at least one contained instruction is speculative;
    /// computed from the maintained <see cref="_speculativeCounter"/> without iterating.
    /// </remarks>
    public override bool IsSpeculative => _speculativeCounter > 0;

    /// <inheritdoc />
    /// <remarks>
    /// Always <c>null</c>: a <see cref="CfgBlock"/> is itself the container, not contained in one.
    /// Attempting to set this throws <see cref="UnhandledCfgDiscrepancyException"/> to surface bugs
    /// where the linker inadvertently tries to nest a block inside another block.
    /// </remarks>
    public override CfgBlock? ContainingBlock {
        get => null;
        set => throw new UnhandledCfgDiscrepancyException(
            $"Cannot set {nameof(ContainingBlock)} on a {nameof(CfgBlock)}.");
    }

    /// <inheritdoc />
    public override HashSet<ICfgNode> Successors => Terminator.Successors;

    /// <inheritdoc />
    public override HashSet<ICfgNode> Predecessors => Entry.Predecessors;

    /// <inheritdoc />
    public override bool CanCauseContextRestore => Terminator.CanCauseContextRestore;

    /// <inheritdoc />
    /// <remarks>
    /// Writing is not supported: a <see cref="CfgBlock"/> has no stored block-edge state.
    /// Any write would corrupt the terminator's value, so this throws to surface the bug.
    /// </remarks>
    public override int? MaxSuccessorsCount {
        get => Terminator.MaxSuccessorsCount;
        set => throw new NotSupportedException(
            $"{nameof(MaxSuccessorsCount)} cannot be set on a {nameof(CfgBlock)}; " +
            $"set it on the underlying terminator ({nameof(CfgInstruction)} or SelectorNode) instead.");
    }

    /// <inheritdoc />
    public override bool CanHaveMoreSuccessors {
        get => Terminator.CanHaveMoreSuccessors;
        set => throw new NotSupportedException(
            $"{nameof(CanHaveMoreSuccessors)} cannot be set on a {nameof(CfgBlock)}; " +
            $"set it on the underlying terminator ({nameof(CfgInstruction)} or SelectorNode) instead.");
    }

    /// <inheritdoc />
    public override ICfgNode? UniqueSuccessor {
        get => Terminator.UniqueSuccessor;
        set => throw new NotSupportedException(
            $"{nameof(UniqueSuccessor)} cannot be set on a {nameof(CfgBlock)}; " +
            $"set it on the underlying terminator ({nameof(CfgInstruction)} or SelectorNode) instead.");
    }

    /// <inheritdoc />
    public override ICfgNode? GetNextSuccessor(InstructionExecutionHelper helper) =>
        Terminator.GetNextSuccessor(helper);

    /// <inheritdoc />
    public override void UpdateSuccessorCache() =>
        throw new NotSupportedException(
            $"{nameof(UpdateSuccessorCache)} cannot be called on a {nameof(CfgBlock)}; " +
            $"the terminator's cache is updated through instruction-level paths directly.");

    /// <inheritdoc />
    /// <remarks>
    /// Returns a <see cref="BlockNode"/> wrapping all contained instructions' display ASTs.
    /// Lazily regenerated when the block's structure changes.
    /// </remarks>
    public override IVisitableAstNode DisplayAst {
        get {
            if (_isDisplayAstStale || _cachedDisplayAst is null) {
                IVisitableAstNode[] statements = new IVisitableAstNode[_instructions.Count];
                for (int i = 0; i < _instructions.Count; i++) {
                    statements[i] = _instructions[i].DisplayAst;
                }
                _cachedDisplayAst = new BlockNode(statements);
                _isDisplayAstStale = false;
            }
            return _cachedDisplayAst;
        }
    }

    /// <inheritdoc />
    public override IVisitableAstNode ExecutionAst =>
        throw new NotSupportedException(
            $"{nameof(ExecutionAst)} is not supported on a {nameof(CfgBlock)}; " +
            $"block-level compiled execution is not yet implemented.");

    // -----------------------------------------------------------------------
    // Internal API used by NodeLinker / CfgInstruction.SetLive only.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Appends <paramref name="node"/> to the end of the block, making it the new
    /// <see cref="Terminator"/>. Updates the non-live and speculative counters from the node's state.
    /// </summary>
    internal void Append(ICfgNode node) {
        _instructions.Add(node);
        if (!node.IsLive) {
            _nonLiveCounter++;
        }
        if (node.IsSpeculative) {
            _speculativeCounter++;
        }
        _isDisplayAstStale = true;
    }

    /// <summary>
    /// Replaces the node at <paramref name="index"/> with <paramref name="newNode"/>, preserving
    /// order. Adjusts the non-live and speculative counters to reflect the new node's state.
    /// </summary>
    internal void ReplaceInPlace(int index, ICfgNode newNode) {
        ICfgNode old = _instructions[index];
        _instructions[index] = newNode;
        if (!old.IsLive) {
            _nonLiveCounter--;
        }
        if (!newNode.IsLive) {
            _nonLiveCounter++;
        }
        if (old.IsSpeculative) {
            _speculativeCounter--;
        }
        if (newNode.IsSpeculative) {
            _speculativeCounter++;
        }
        _isDisplayAstStale = true;
    }

    /// <summary>
    /// Removes <paramref name="node"/> from the block by value, maintaining the non-live and
    /// speculative counters via O(1) incremental decrement (mirrors <see cref="ReplaceInPlace"/>).
    /// Returns <c>false</c> when the node is not part of the block. Tolerates transient
    /// non-contiguity: it is a batch-internal primitive for removing a contiguous suffix of
    /// nodes one at a time.
    /// </summary>
    internal bool Remove(ICfgNode node) {
        if (!_instructions.Remove(node)) {
            return false;
        }
        if (!node.IsLive) {
            _nonLiveCounter--;
        }
        if (node.IsSpeculative) {
            _speculativeCounter--;
        }
        _isDisplayAstStale = true;
        return true;
    }

    /// <summary>
    /// Removes nodes from <paramref name="splitIndex"/> through the end and returns them as a
    /// new list. The non-live and speculative counters are recomputed for the surviving prefix.
    /// </summary>
    internal List<ICfgNode> SliceFrom(int splitIndex) {
        int tailLength = _instructions.Count - splitIndex;
        List<ICfgNode> tail = _instructions.GetRange(splitIndex, tailLength);
        _instructions.RemoveRange(splitIndex, tailLength);
        RecountNonLiveFromInstructions();
        RecountSpeculativeFromInstructions();
        _isDisplayAstStale = true;
        return tail;
    }

    /// <summary>
    /// Counter choke-point invoked by <see cref="CfgInstruction.SetLive"/> exactly once per
    /// actual <see cref="ICfgNode.IsLive"/> transition of a contained instruction.
    /// </summary>
    internal void OnContainedInstructionLiveChanged(bool nowLive) {
        if (nowLive) {
            _nonLiveCounter--;
        } else {
            _nonLiveCounter++;
        }
    }

    /// <summary>
    /// Counter choke-point invoked by <see cref="CfgInstruction.SetSpeculative"/> exactly once per
    /// actual <see cref="ICfgNode.IsSpeculative"/> transition of a contained instruction.
    /// </summary>
    internal void OnContainedInstructionSpeculativeChanged(bool nowSpeculative) {
        if (nowSpeculative) {
            _speculativeCounter++;
        } else {
            _speculativeCounter--;
        }
    }

    /// <summary>
    /// Recomputes the non-live counter from scratch by iterating the contained instructions.
    /// Used after a split to re-base the counter on the new instruction set.
    /// </summary>
    internal void RecountNonLiveFromInstructions() {
        _nonLiveCounter = _instructions.Count(node => !node.IsLive);
    }

    /// <summary>
    /// Recomputes the speculative counter from scratch by iterating the contained instructions.
    /// Used after a split to re-base the counter on the new instruction set.
    /// </summary>
    internal void RecountSpeculativeFromInstructions() {
        _speculativeCounter = _instructions.Count(node => node.IsSpeculative);
    }

    /// <summary>
    /// Returns the index of <paramref name="node"/> in the contained list, or <c>-1</c> if
    /// the node is not part of this block.
    /// </summary>
    internal int IndexOf(ICfgNode node) => _instructions.IndexOf(node);
}
