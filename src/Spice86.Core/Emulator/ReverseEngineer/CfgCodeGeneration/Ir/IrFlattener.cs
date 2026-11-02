namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Turns the lowered tree of one method into flat <see cref="IrBlock"/>s: one block per instruction start,
/// plus the inner blocks an instruction's own control flow lowers to (branches, loops, switches). Every
/// edge inside one instruction is <see cref="IrEdge.InsideInstruction"/>; only a <see cref="GotoNode"/> to
/// another CFG node produces an instruction edge.
/// </summary>
internal sealed class IrFlattener {
    private readonly bool _raw;
    private readonly List<IrBlock> _blocks = [];
    private readonly Stack<Func<IrBlock>> _breakTargets = new();
    private readonly List<PendingJump> _pendingJumps = [];
    private IrBlock? _current;

    /// <summary>Flattens a lowered method tree (<paramref name="raw"/> false) or one raw execution AST
    /// (<paramref name="raw"/> true): in raw mode the runtime transfer nodes become
    /// <see cref="IrExit"/>s and <see cref="WhileWithExitNode"/> lowers to a header branch plus an
    /// exit-condition branch at the body tail.</summary>
    public IrFlattener(bool raw) {
        _raw = raw;
    }

    /// <summary>The blocks created so far, in creation order (instruction layout order).</summary>
    public List<IrBlock> Blocks => _blocks;

    /// <summary>The instruction edges waiting for their target's start block; resolved after the walk.</summary>
    public IReadOnlyList<PendingJump> PendingJumps => _pendingJumps;

    /// <summary>
    /// Whether the block being filled has no statement and no terminator: the next node can stay in it even
    /// when it starts a new CFG block.
    /// </summary>
    public bool CurrentIsEmpty => _current == null || (!_current.HasTerminator && _current.Statements.Count == 0);

    /// <summary>An instruction edge to resolve once every start block is known.</summary>
    /// <param name="From">The block the edge leaves.</param>
    /// <param name="Origin">The instruction the edge comes from.</param>
    /// <param name="Target">The CFG node the edge jumps to.</param>
    internal sealed record PendingJump(IrBlock From, ICfgNode Origin, ICfgNode Target);

    /// <summary>
    /// Starts the block <paramref name="node"/> flattens into: a fresh block when the node opens a CFG block
    /// or method entry and the current one has content, or when the previous transfer left no open block.
    /// An inner block (the inside-edge target of a previous instruction's control flow) never receives an
    /// instruction start; it is closed with an instruction edge to the new block instead.
    /// </summary>
    public IrBlock StartBlock(ICfgNode node, bool opensBoundary, ICfgNode? previousOrigin) {
        if (_current == null) {
            _current = NewBlock(false);
            return _current;
        }
        bool needsNew = opensBoundary ? !CurrentIsEmpty : _inner.Contains(_current);
        if (!needsNew) {
            return _current;
        }
        IrBlock next = NewBlock(false);
        if (!_current.HasTerminator) {
            _current.Terminator = new IrJump(previousOrigin ?? node, new IrEdge(next, false));
        }
        _current = next;
        return _current;
    }

    /// <summary>Appends a boundary or plain statement to the current block, typing and renaming it.</summary>
    public void Add(IVisitableAstNode statement, ICfgNode origin, string localSuffix) {
        if (_current == null || _current.HasTerminator) {
            throw new InvalidOperationException($"Statement added after the block of {origin.Address} was closed.");
        }
        _current.Statements.Add(IrStatement.Create(IrNodeTyper.TypeStatement(statement, localSuffix), origin));
    }

    /// <summary>Flattens the statements of <paramref name="node"/> into blocks starting at the current one.</summary>
    /// <param name="isLocalFallthrough">Whether a <see cref="GotoNode"/> target is the next plan node in the
    /// same block, making the goto a plain fallthrough with no IR edge.</param>
    public void Flatten(IVisitableAstNode node, ICfgNode origin, string localSuffix,
        Func<ICfgNode, bool> isLocalFallthrough) {
        AstTrampoline.Run(FlattenIt(node, origin, localSuffix, isLocalFallthrough));
    }

    /// <summary>
    /// The trampolined form of the flattening walk: every recursive descent into a nested statement is a
    /// yielded <see cref="AstSubcall"/>, so arbitrarily deep <c>if</c> / <c>else</c> chains cannot overflow
    /// the stack. Block creation order is the recursive walk's.
    /// </summary>
    private IEnumerable<AstSubcall> FlattenIt(IVisitableAstNode node, ICfgNode origin, string localSuffix,
        Func<ICfgNode, bool> isLocalFallthrough) {
        foreach (IVisitableAstNode statement in StatementsOf(node)) {
            yield return AstTrampoline.Call(FlattenStatementIt(statement, origin, localSuffix, isLocalFallthrough));
        }
    }

    private IEnumerable<AstSubcall> FlattenStatementIt(IVisitableAstNode statement, ICfgNode origin,
        string localSuffix, Func<ICfgNode, bool> isLocalFallthrough) {
        if (_current == null) {
            throw new NotSupportedException($"Statements after a transfer out of {origin.Address}: {statement.GetType().Name}.");
        }
        switch (statement) {
            case BlockNode block:
                yield return AstTrampoline.Call(FlattenIt(block, origin, localSuffix, isLocalFallthrough));
                break;
            case IfElseNode ifElse:
                yield return AstTrampoline.Call(FlattenIfElseIt(ifElse, origin, localSuffix, isLocalFallthrough));
                break;
            case WhileNode whileNode:
                yield return AstTrampoline.Call(FlattenWhileIt(whileNode, origin, localSuffix, isLocalFallthrough));
                break;
            case SwitchNode switchNode:
                yield return AstTrampoline.Call(FlattenSwitchIt(switchNode, origin, localSuffix, isLocalFallthrough));
                break;
            case BreakNode:
                EndBlock(_current, new IrJump(origin, new IrEdge(_breakTargets.Peek()(), true)));
                break;
            case GotoNode { Target: not CfgNodeGotoTarget }:
                throw new NotSupportedException($"GotoNode target {statement} at {origin.Address} is not a CFG node.");
            case GotoNode { Target: CfgNodeGotoTarget cfgTarget } when isLocalFallthrough(cfgTarget.Node):
                break;
            case GotoNode { Target: CfgNodeGotoTarget cfgTarget }:
                _pendingJumps.Add(new PendingJump(_current, origin, cfgTarget.Node));
                _current = null;
                break;
            case ReturnNode or FailAsUntestedNode or ThrowNode or CyclicJumpNode or InterruptReturnNode:
                EndBlock(_current, new IrExit(origin, IrNodeTyper.RenameTemporaries(statement, localSuffix)));
                break;
            case WhileWithExitNode whileWithExit when _raw:
                yield return AstTrampoline.Call(FlattenWhileWithExitIt(whileWithExit, origin, localSuffix, isLocalFallthrough));
                break;
            case JumpNearNode or JumpFarNode or CallNearNode or CallFarNode or MoveIpNextNode
                or HltNode or InvalidInstructionNode or CpuidNode or InterruptCallNode
                or ReturnNearNode or ReturnFarNode or ReturnInterruptNode or CallbackNode when _raw:
                EndBlock(_current, new IrExit(origin, statement));
                break;
            case WhileWithExitNode or InfiniteLoopNode or DoWhileNode or ContinueNode or LabelNode
                or CpuFaultTryCatchNode or InstructionBoundaryNode or EntryDispatcherLabelNode
                or InstructionNode or JumpNearNode or JumpFarNode or CallNearNode or CallFarNode
                or MoveIpNextNode or HltNode or SelectorNode or InvalidInstructionNode or CpuidNode
                or InterruptCallNode or ReturnNearNode or ReturnFarNode or ReturnInterruptNode
                or CallbackNode:
                throw new NotSupportedException($"Lowered node {statement.GetType().Name} cannot appear inside an instruction at {origin.Address}.");
            default:
                _current.Statements.Add(IrStatement.Create(IrNodeTyper.TypeStatement(statement, localSuffix), origin));
                break;
        }
    }

    private IEnumerable<AstSubcall> FlattenIfElseIt(IfElseNode node, ICfgNode origin, string localSuffix,
        Func<ICfgNode, bool> isLocalFallthrough) {
        IrBlock from = _current ?? throw new InvalidOperationException(
            $"Flatten reached {origin.Address} with no open block.");
        IrBlock? join = null;
        IrBlock Join() => join ??= NewBlock(true);
        ValueNode condition = IrNodeTyper.TypeValue(node.Condition, localSuffix);
        AstResult<IrBlock> whenTrue = new();
        yield return AstTrampoline.Call(FlattenArmIt(node.TrueCase, origin, localSuffix, isLocalFallthrough, Join, whenTrue));
        AstResult<IrBlock> whenFalse = new();
        yield return AstTrampoline.Call(FlattenArmIt(node.FalseCase, origin, localSuffix, isLocalFallthrough, Join, whenFalse));
        from.Terminator = new IrBranch(origin, condition, new IrEdge(whenTrue.Value!, true), new IrEdge(whenFalse.Value!, true));
        _current = join;
    }

    /// <summary>
    /// Flattens one arm into <paramref name="result"/>: the block it starts in, or the join block for an
    /// empty arm. An arm that completes normally ends with an inside jump to the join.
    /// </summary>
    private IEnumerable<AstSubcall> FlattenArmIt(IVisitableAstNode arm, ICfgNode origin, string localSuffix,
        Func<ICfgNode, bool> isLocalFallthrough, Func<IrBlock> join, AstResult<IrBlock> result) {
        if (StatementsOf(arm).Count == 0) {
            result.Value = join();
            yield break;
        }
        IrBlock start = NewBlock(true);
        _current = start;
        yield return AstTrampoline.Call(FlattenIt(arm, origin, localSuffix, isLocalFallthrough));
        if (_current is { HasTerminator: false } tail) {
            tail.Terminator = new IrJump(origin, new IrEdge(join(), true));
        }
        result.Value = start;
    }

    /// <summary>
    /// A raw <see cref="WhileWithExitNode"/>: a header testing <paramref name="node"/>'s condition, a body
    /// that ends testing its exit condition (exit or back to the header), and an exit block.
    /// </summary>
    private IEnumerable<AstSubcall> FlattenWhileWithExitIt(WhileWithExitNode node, ICfgNode origin,
        string localSuffix, Func<ICfgNode, bool> isLocalFallthrough) {
        IrBlock from = _current ?? throw new InvalidOperationException(
            $"Flatten reached {origin.Address} with no open block.");
        IrBlock header = NewBlock(true);
        IrBlock body = NewBlock(true);
        IrBlock exit = NewBlock(true);
        from.Terminator = new IrJump(origin, new IrEdge(header, true));
        header.Terminator = new IrBranch(origin, IrNodeTyper.TypeValue(node.Condition, localSuffix),
            new IrEdge(body, true), new IrEdge(exit, true));
        _breakTargets.Push(() => exit);
        try {
            _current = body;
            yield return AstTrampoline.Call(FlattenIt(node.Body, origin, localSuffix, isLocalFallthrough));
        } finally {
            _breakTargets.Pop();
        }
        if (_current is not { HasTerminator: false } tail) {
            throw new NotSupportedException(
                $"WhileWithExitNode at {origin.Address} ends its body in a transfer; the exit condition has no block.");
        }
        tail.Terminator = new IrBranch(origin, IrNodeTyper.TypeValue(node.ExitCondition, localSuffix),
            new IrEdge(exit, true), new IrEdge(header, true));
        _current = exit;
    }

    private IEnumerable<AstSubcall> FlattenWhileIt(WhileNode node, ICfgNode origin, string localSuffix,
        Func<ICfgNode, bool> isLocalFallthrough) {
        IrBlock from = _current ?? throw new InvalidOperationException(
            $"Flatten reached {origin.Address} with no open block.");
        IrBlock header = NewBlock(true);
        IrBlock body = NewBlock(true);
        IrBlock exit = NewBlock(true);
        from.Terminator = new IrJump(origin, new IrEdge(header, true));
        header.Terminator = new IrBranch(origin, IrNodeTyper.TypeValue(node.Condition, localSuffix),
            new IrEdge(body, true), new IrEdge(exit, true));
        _breakTargets.Push(() => exit);
        try {
            _current = body;
            yield return AstTrampoline.Call(FlattenIt(node.Body, origin, localSuffix, isLocalFallthrough));
        } finally {
            _breakTargets.Pop();
        }
        if (_current is { HasTerminator: false } tail) {
            tail.Terminator = new IrJump(origin, new IrEdge(header, true));
        }
        _current = exit;
    }

    private IEnumerable<AstSubcall> FlattenSwitchIt(SwitchNode node, ICfgNode origin, string localSuffix,
        Func<ICfgNode, bool> isLocalFallthrough) {
        IrBlock from = _current ?? throw new InvalidOperationException(
            $"Flatten reached {origin.Address} with no open block.");
        IrBlock? join = null;
        IrBlock Join() => join ??= NewBlock(true);
        List<IrSwitchCase> cases = [];
        AstResult<IrBlock> defaultBlock = new();
        _breakTargets.Push(Join);
        try {
            foreach (SwitchSection section in node.Sections) {
                AstResult<IrBlock> start = new();
                yield return AstTrampoline.Call(FlattenArmIt(section.Body, origin, localSuffix, isLocalFallthrough, Join, start));
                cases.Add(new IrSwitchCase(section.Label, new IrEdge(start.Value!, true)));
            }
            yield return AstTrampoline.Call(FlattenArmIt(node.Default, origin, localSuffix, isLocalFallthrough, Join, defaultBlock));
        } finally {
            _breakTargets.Pop();
        }
        from.Terminator = new IrSwitch(origin, IrNodeTyper.TypeValue(node.Value, localSuffix), cases,
            new IrEdge(defaultBlock.Value!, true));
        _current = join;
    }

    private void EndBlock(IrBlock block, IrTerminator terminator) {
        block.Terminator = terminator;
        _current = null;
    }

    private readonly HashSet<IrBlock> _inner = [];

    private IrBlock NewBlock(bool inner) {
        IrBlock block = new(_blocks.Count);
        _blocks.Add(block);
        if (inner) {
            _inner.Add(block);
        }
        return block;
    }
}
