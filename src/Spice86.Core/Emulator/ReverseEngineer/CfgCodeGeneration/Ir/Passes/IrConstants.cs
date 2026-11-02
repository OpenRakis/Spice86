namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;

using System.Linq;

/// <summary>
/// A sparse conditional constant propagation over the IR SSA. General-register versions hold a
/// per-byte lattice (undetermined / known value / varying); only edges reachable from an entry are
/// executable, an <see cref="IrBranch"/> whose condition evaluates to a constant marks only the taken
/// arm's edge, and phis meet the operands of executable predecessors. Reads of known-clean register
/// versions are substituted inside <see cref="AluOperationNode"/> / <see cref="AluStatementNode"/>
/// arguments and the port argument of <see cref="PortReadCallNode"/> / <see cref="PortWriteCallNode"/>, never
/// on the left of an assignment, never inside a loop block or a division / rep compare shape, and
/// never in an instruction that can fault or has an after-check. The per-byte result is kept in
/// <see cref="IrConstantFacts"/> for the branch pass.
///
/// <para>The direction flag is a second lattice value next to the general registers: a one-bit
/// entry per <see cref="IrSsaValue.Direction"/> version, known on a constant
/// <c>DirectionFlag = x</c> or flag-register write (bit 10), varying on any other definition, met
/// through phis like the registers. A hardware interrupt pushes FLAGS and its <c>iret</c> restores
/// them, so <see cref="CheckExternalEventsNode"/> does not dirty the direction flag; its effect
/// may-writes every general register a handler can observe, which gives them new versions the
/// lattice does not know, so nothing here special-cases interrupt delivery. No statement inside a
/// string instruction writes the direction flag either: a known
/// direction folds at every direction step, including <c>rep</c> bodies and faulting instructions
/// - the refusal rules above are about register versions, not flag versions. A
/// <see cref="ConditionalValueNode"/> whose condition the facts evaluate to a constant rewrites to
/// its chosen arm (the bare direction-flag read of a step folds even inside a loop); the boolean
/// identities are shared with <see cref="IrBranchSimplification"/> so a condition simplifies the
/// same way in both places.</para>
///
/// <para>A declaration no statement reads anymore is removed only when the rewritten statement's
/// stored effects say it is side-effect free: it writes no machine value (<see cref="IrValues"/>),
/// writes no token (<see cref="IrTokens"/>), cannot fault (<see cref="IrEffects.MayFault"/>) and
/// delivers no interrupts (<see cref="IrEffects.DeliversInterrupts"/>).</para>
/// </summary>
internal static class IrConstants {
    /// <summary>Computes the constants of <paramref name="method"/>, substitutes the reads they make
    /// known, and returns the facts.</summary>
    /// <param name="graph">The structuring graph of <paramref name="method"/>, built once for its
    /// current block structure.</param>
    public static IrConstantFacts Apply(IrMethod method, IrSsa ssa,
        BlockGraph<IrBlock> graph) {
        IrConstantFacts facts = new(ssa);
        ComputeClean(facts, graph);
        new IrSccpSolver(method, ssa, facts).Compute();
        Substitute(method, ssa, facts);
        return facts;
    }

    // Clean analysis: per position, the registers whose current version was defined with no interrupt
    // delivery, no unknown-like effect and no method entry on every path since.
    private static void ComputeClean(IrConstantFacts facts,
        BlockGraph<IrBlock> graph) {
        DepthFirstOrdering<IrBlock> traversal = new(
            [.. graph.EntryBlocks, .. graph.Blocks], graph.GetSuccessors);
        ForwardDataflowResult<IrBlock, uint> result = ForwardDataflowSolver.Solve(
            graph, traversal,
            (block, state) => OutState(block, state, null),
            (first, second) => first & second,
            IrConstantFacts.GeneralRegisters, 0u);
        foreach (IrBlock block in graph.Blocks) {
            OutState(block, result.In[block], facts);
        }
    }

    /// <summary>The clean set after <paramref name="block"/>: a definition cleans what it writes and
    /// an unknown-like effect (may-writing every general register) dirties everything after the
    /// statement. An event check needs no case of its own: its may-write of the handler-visible
    /// registers gives them new versions whose value the lattice does not know. Records the position
    /// masks into <paramref name="facts"/> when given.</summary>
    private static uint OutState(IrBlock block, uint state, IrConstantFacts? facts) {
        for (int index = 0; index < block.Statements.Count; index++) {
            IrStatement statement = block.Statements[index];
            facts?.SetClean(block, index, state);
            state |= DefinedGeneralRegisters(statement.Effects);
            if (UnknownLike(statement.Effects)) {
                state = 0;
            }
        }
        facts?.SetClean(block, block.Statements.Count, state);
        IrEffects terminator = block.Terminator.Effects;
        state |= DefinedGeneralRegisters(terminator);
        if (UnknownLike(terminator)) {
            state = 0;
        }
        return state;
    }

    private static uint DefinedGeneralRegisters(IrEffects effects) {
        uint mask = 0;
        IrValues writes = effects.MustWrite | effects.MayWrite;
        for (int v = 0; v <= (int)IrSsaValue.Edi; v++) {
            if ((writes & IrSsaValues.MaskOf((IrSsaValue)v)) != IrValues.None) {
                mask |= 1u << v;
            }
        }
        return mask;
    }

    /// <summary>Effects of code the analysis does not know: it may write every general register.</summary>
    private static bool UnknownLike(IrEffects effects) =>
        (effects.MayWrite & GeneralRegistersMask) == GeneralRegistersMask;

    private const IrValues GeneralRegistersMask =
        (IrValues)((1UL << (IrConstantFacts.ByteCount * 8)) - 1);

    // Substitution: reads of known versions, in the substitutable places only.
    private static void Substitute(IrMethod method, IrSsa ssa, IrConstantFacts facts) {
        HashSet<IrBlock> loopBlocks = LoopBlocks(method);
        List<(IrBlock Block, int Index, IrStatement Original, IVisitableAstNode Node)> rewritten = [];
        foreach ((ICfgNode origin, List<IrOrigins.Located> statements) in IrOrigins.ByOrigin(method)) {
            bool rewritable = IrOrigins.Rewritable(statements, origin)
                && method.Shapes.GetValueOrDefault(origin) is not IrInstructionShape.Division;
            bool repCompare = method.Shapes.GetValueOrDefault(origin)
                is IrInstructionShape.RepCompare;
            Dictionary<string, ConstantNode> variableConstants = new();
            foreach (IrOrigins.Located located in statements) {
                IVisitableAstNode node = located.Statement.Node;
                bool inLoop = loopBlocks.Contains(located.Block);
                if (rewritable) {
                    node = IrConstantFolder.Fold(Rewrite(node, located.Block, located.Index, facts,
                        !inLoop && !repCompare),
                        inLoop ? IrConstantFolder.NoVariableConstants : variableConstants);
                }
                rewritten.Add((located.Block, located.Index, located.Statement, node));
                if (!inLoop && node is VariableDeclarationNode declaration) {
                    if (ConstantEvaluation.TryEvaluate(declaration.Initializer, _ => null,
                            out ulong variableValue)) {
                        variableConstants[declaration.VariableName] = new ConstantNode(
                            declaration.DataType,
                            ConstantEvaluation.Bits(declaration.DataType, variableValue));
                    } else {
                        variableConstants.Remove(declaration.VariableName);
                    }
                }
            }
        }
        ApplySubstitutions(method, ssa, rewritten);
    }

    /// <summary>Applies <paramref name="rewritten"/>: a declaration nothing reads is removed when its
    /// rewritten statement has no side effect; every other changed statement replaces its site. A
    /// method variable can be declared by one instruction's statements and read by another's, so the
    /// reference census covers the whole method.</summary>
    private static void ApplySubstitutions(IrMethod method, IrSsa ssa,
        List<(IrBlock Block, int Index, IrStatement Original, IVisitableAstNode Node)> rewritten) {
        Dictionary<string, int> references = new();
        foreach ((_, _, _, IVisitableAstNode node) in rewritten) {
            CountVariableReferences(node, references);
        }
        foreach (IrBlock block in method.Blocks) {
            CountVariableReferences(block.Terminator, references);
        }
        List<(IrBlock Block, int Index, IrStatement? Replacement)> edits = [];
        foreach ((IrBlock block, int index, IrStatement original, IVisitableAstNode node)
            in rewritten) {
            if (node is VariableDeclarationNode declaration
                && references.GetValueOrDefault(declaration.VariableName) == 0
                && HasNoSideEffect(node, original.Origin)) {
                edits.Add((block, index, null));
                continue;
            }
            if (!ReferenceEquals(node, original.Node)) {
                edits.Add((block, index, IrStatement.Create(node, original.Origin)));
            }
        }
        foreach ((IrBlock block, int index, IrStatement? replacement)
            in edits.OrderByDescending(edit => edit.Index)) {
            IrStatement old = block.Statements[index];
            block.Statements.RemoveAt(index);
            if (replacement is null) {
                ssa.Remove(old);
                continue;
            }
            block.Statements.Insert(index, replacement);
            if (ssa.HasSite(old)) {
                ssa.Replace(old, replacement);
            }
        }
    }

    /// <summary>The blocks of an inside-instruction loop (a <c>WhileNode</c>/<c>WhileWithExitNode</c> of one
    /// instruction's body): they run again on every iteration, so a version known at the header does not
    /// stay known. Blocks of a method-level loop are not refused: a register constant on every path into
    /// the instruction still holds there. A block loops exactly when its strongly connected component on the
    /// <see cref="IrEdge.InsideInstruction"/> edges holds more than one block, or when it has an inside edge
    /// to itself.</summary>
    private static HashSet<IrBlock> LoopBlocks(IrMethod method) {
        static IEnumerable<IrBlock> InsideTargets(IrBlock block) =>
            block.Terminator.Edges.Where(edge => edge.InsideInstruction).Select(edge => edge.Target);
        Dictionary<IrBlock, int> componentByBlock =
            StronglyConnectedComponents.Find(method.Blocks, InsideTargets);
        Dictionary<int, int> sizeByComponent = componentByBlock.Values
            .GroupBy(component => component)
            .ToDictionary(group => group.Key, group => group.Count());
        HashSet<IrBlock> loops = [.. method.Blocks.Where(block =>
            sizeByComponent[componentByBlock[block]] > 1
            || InsideTargets(block).Any(target => target == block))];
        return loops;
    }

    private static IVisitableAstNode Rewrite(IVisitableAstNode node, IrBlock block, int position,
        IrConstantFacts facts, bool substituteRegisters) {
        switch (node) {
            case RegisterNode register:
                if (!substituteRegisters
                    || !facts.TryGetBefore(block, position, register, out ulong constant)) {
                    return node;
                }
                return new ConstantNode(register.DataType, constant);
            case BinaryOperationNode binary:
                return RewriteBinary(binary, block, position, facts, substituteRegisters);
            case UnaryOperationNode or TypeConversionNode or SegmentedPointerNode
                or AbsolutePointerNode or SegmentedAddressNode:
                return IrAstChildren.MapChildren(node,
                    child => Rewrite(child, block, position, facts, substituteRegisters));
            case ConditionalValueNode conditional:
                return RewriteConditional(conditional, block, position, facts, substituteRegisters);
            case PortReadCallNode:
                return IrAstChildren.MapChildren(node,
                    child => Rewrite(child, block, position, facts, substituteRegisters));
            case PortWriteCallNode portWrite:
                return RewriteArguments(portWrite, block, position, facts, substituteRegisters, 1);
            case MethodCallValueNode:
            case MethodCallNode:
                return node;
            case AluOperationNode or AluStatementNode:
                return IrAstChildren.MapChildren(node,
                    child => Rewrite(child, block, position, facts, substituteRegisters));
            case IfElseNode ifElse:
                IVisitableAstNode condition = RewriteCondition(ifElse.Condition, block, position, facts, substituteRegisters);
                return ReferenceEquals(condition, ifElse.Condition) ? node
                    : new IfElseNode((ValueNode)condition, ifElse.TrueCase, ifElse.FalseCase);
            case WhileNode whileNode:
                BlockNode whileBody = RewriteBody(whileNode.Body, block, position, facts);
                return ReferenceEquals(whileBody, whileNode.Body) ? node
                    : new WhileNode(whileNode.Condition, whileBody);
            case WhileWithExitNode whileWithExit:
                BlockNode exitBody = RewriteBody(whileWithExit.Body, block, position, facts);
                return ReferenceEquals(exitBody, whileWithExit.Body) ? node
                    : new WhileWithExitNode(whileWithExit.Condition, exitBody,
                        whileWithExit.ExitCondition);
            default:
                return node;
        }
    }

    private static IVisitableAstNode RewriteConditional(ConditionalValueNode conditional,
        IrBlock block, int position, IrConstantFacts facts, bool substituteRegisters) {
        ConditionalValueNode rebuilt = (ConditionalValueNode)IrAstChildren.MapChildren(conditional,
            child => Rewrite(child, block, position, facts, substituteRegisters));
        // Inside a loop only a bare flag read folds: a register fact could go stale across
        // iterations, a flag version cannot.
        return (substituteRegisters || rebuilt.Condition is CpuFlagNode)
            && facts.TryEvaluate(rebuilt.Condition, block, position, out ulong taken)
            ? taken != 0 ? rebuilt.WhenTrue : rebuilt.WhenFalse
            : rebuilt;
    }

    private static IVisitableAstNode RewriteBinary(BinaryOperationNode binary, IrBlock block,
        int position, IrConstantFacts facts, bool substituteRegisters) {
        IVisitableAstNode left = binary.BinaryOperation == BinaryOperation.ASSIGN
            && binary.Left is RegisterNode
            ? binary.Left
            : Rewrite(binary.Left, block, position, facts, substituteRegisters);
        IVisitableAstNode right = Rewrite(binary.Right, block, position, facts, substituteRegisters);
        return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right) ? binary
            : new BinaryOperationNode(binary.DataType, (ValueNode)left, binary.BinaryOperation,
                (ValueNode)right);
    }

    private static IVisitableAstNode RewriteCondition(IVisitableAstNode condition, IrBlock block,
        int position, IrConstantFacts facts, bool substituteRegisters) {
        IVisitableAstNode rewritten = IrConstantFolder.Fold(
            Rewrite(condition, block, position, facts, substituteRegisters),
            IrConstantFolder.NoVariableConstants);
        return ContainsStateRead(rewritten) ? rewritten : condition;
    }

    /// <summary>Rewrites the statements of an inside-instruction loop body: register versions do not
    /// stay known across iterations, so only the bare-flag conditional folds there.</summary>
    private static BlockNode RewriteBody(BlockNode body, IrBlock block, int position,
        IrConstantFacts facts) =>
        (BlockNode)IrAstChildren.MapChildren(body,
            statement => Rewrite(statement, block, position, facts, false));

    /// <summary>Rewrites the first <paramref name="lastArgument"/> arguments of <paramref name="methodCall"/>.</summary>
    private static MethodCallNode RewriteArguments(MethodCallNode methodCall, IrBlock block,
        int position, IrConstantFacts facts, bool substituteRegisters, int lastArgument) {
        List<IVisitableAstNode> arguments = new(methodCall.Arguments.Count);
        bool changed = false;
        for (int index = 0; index < methodCall.Arguments.Count; index++) {
            IVisitableAstNode argument = methodCall.Arguments[index];
            IVisitableAstNode rewritten = index < lastArgument
                ? Rewrite(argument, block, position, facts, substituteRegisters)
                : argument;
            arguments.Add(rewritten);
            changed |= !ReferenceEquals(rewritten, argument);
        }
        return changed
            ? methodCall.WithArguments([.. arguments])
            : methodCall;
    }

    private static void CountVariableReferences(IrTerminator terminator,
        Dictionary<string, int> references) {
        foreach (IVisitableAstNode node in IrAstChildren.RootsOf(terminator)) {
            CountVariableReferences(node, references);
        }
    }

    private static void CountVariableReferences(IVisitableAstNode node,
        Dictionary<string, int> references) {
        foreach (VariableReferenceNode reference in IrAstChildren.Descendants(node).OfType<VariableReferenceNode>()) {
            references[reference.VariableName] = references.GetValueOrDefault(reference.VariableName) + 1;
        }
    }

    /// <summary>Whether a declaration with no reader may be dropped: the rewritten statement's
    /// stored effects say it is side-effect free — it writes no machine value (<see cref="IrValues"/>),
    /// writes no token (<see cref="IrTokens"/>), cannot fault (<see cref="IrEffects.MayFault"/>) and
    /// delivers no interrupts (<see cref="IrEffects.DeliversInterrupts"/>).</summary>
    private static bool HasNoSideEffect(IVisitableAstNode node, ICfgNode origin) {
        IrEffects effects = IrStatement.Create(node, origin).Effects;
        return effects.MayWrite == IrValues.None && effects.TokenWrites == IrTokens.None
            && !effects.MayFault && !effects.DeliversInterrupts;
    }

    /// <summary>Whether <paramref name="node"/> still depends on machine state after folding: it
    /// reads a machine value (<see cref="IrValues"/>) or a token (<see cref="IrTokens"/>), writes
    /// either, or mentions a temporary, which the effects do not track.</summary>
    internal static bool ContainsStateRead(IVisitableAstNode node) {
        IrEffects effects = IrEffectsVisitor.Of(node, false);
        if (effects.Reads != IrValues.None || effects.TokenReads != IrTokens.None
            || effects.MayWrite != IrValues.None || effects.TokenWrites != IrTokens.None) {
            return true;
        }
        return IrAstChildren.Descendants(node).Any(descendant => descendant is VariableReferenceNode);
    }
}
