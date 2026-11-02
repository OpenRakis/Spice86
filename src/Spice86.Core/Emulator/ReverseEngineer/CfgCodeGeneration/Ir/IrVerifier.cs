namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// Checks the invariants of an <see cref="IrMethod"/>; the lifter runs it once per method and passes run
/// it on demand. A violation throws <see cref="InvalidOperationException"/> naming the method, the block
/// id, the origin address and the rule.
/// </summary>
internal static class IrVerifier {
    /// <summary>Verifies <paramref name="method"/>.</summary>
    /// <exception cref="InvalidOperationException">A rule is violated.</exception>
    public static void Verify(IrMethod method) {
        VerifyWellFormed(method);
        VerifyStoredEffects(method);
        VerifyInstructionEdges(method);
        VerifyEventChecks(method);
        VerifyFaults(method);
        VerifyTemporaries(method);
        VerifyEntries(method);
        VerifyConditionalArms(method);
    }

    /// <summary>A conditional value is a pure select: both arms must evaluate without side effects,
    /// so constants, registers, flags, variables and operations on them are allowed but no memory
    /// read, helper call or anything that can fault. Only then is keeping it an expression (and
    /// folding it to the chosen arm) correct.</summary>
    private static void VerifyConditionalArms(IrMethod method) {
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements
                    .Where(statement => HasImpureConditionalArm(statement.Node))) {
                Fail(method, block, statement.Origin,
                    "conditional value arm is not a pure value (conditional arms)");
            }
            if (IrAstChildren.RootsOf(block.Terminator).OfType<ConditionalValueNode>()
                    .Any(conditional => !IsPureArm(conditional.WhenTrue)
                        || !IsPureArm(conditional.WhenFalse))) {
                Fail(method, block, block.Terminator.Origin,
                    "conditional value arm is not a pure value (conditional arms)");
            }
        }
    }

    private static bool HasImpureConditionalArm(IVisitableAstNode node) =>
        IrAstChildren.Descendants(node).OfType<ConditionalValueNode>()
            .Any(conditional => !IsPureArm(conditional.WhenTrue) || !IsPureArm(conditional.WhenFalse));

    private static bool IsPureArm(ValueNode node) =>
        IrAstChildren.Descendants(node).All(IsPureArmNode);

    private static bool IsPureArmNode(IVisitableAstNode node) => node switch {
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN or BinaryOperation.DIVIDE
            or BinaryOperation.MODULO } => false,
        ConstantNode or RegisterNode or SegmentRegisterNode or CpuFlagNode or FlagRegisterNode
            or VariableReferenceNode or UnaryOperationNode or BinaryOperationNode
            or TypeConversionNode or ExplicitCastNode or ConditionalValueNode or NearAddressNode
            or InstructionFieldNode => true,
        _ => false
    };

    private static void VerifyWellFormed(IrMethod method) {
        HashSet<int> ids = [];
        HashSet<IrBlock> blocks = [.. method.Blocks];
        foreach (IrBlock block in method.Blocks) {
            if (!ids.Add(block.Id)) {
                Fail(method, block, "duplicate block id (well formed)");
            }
            if (!block.HasTerminator) {
                Fail(method, block, "block has no terminator (well formed)");
            }
            if (block.Terminator.Edges.Any(edge => !blocks.Contains(edge.Target))) {
                Fail(method, block, $"edge targets a block outside the method (well formed)");
            }
            foreach (IrStatement statement in block.Statements) {
                CheckStatementShape(method, block, statement);
            }
        }
        foreach (IrEntry entry in method.Entries.Where(entry => !blocks.Contains(entry.Block))) {
            Fail(method, entry.Block, "entry block is not a block of the method (well formed)");
        }
    }

    private static void CheckStatementShape(IrMethod method, IrBlock block, IrStatement statement) {
        if (statement.Node is OpaqueNode opaque) {
            Fail(method, block, statement.Origin,
                $"unhandled statement lowered to OpaqueNode: {opaque.Original.GetType().Name} (opaque)");
        }
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(statement.Node).Where(IsForbiddenInIr)) {
            Fail(method, block, statement.Origin, $"{node.GetType().Name} is not an IR statement (well formed)");
        }
    }

    private static void VerifyStoredEffects(IrMethod method) {
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                IrEffects expected = IrEffectsVisitor.Of(statement.Node,
                    IrStatement.MayFault(statement.Node, statement.Origin));
                if (!statement.Effects.Equals(expected)) {
                    Fail(method, block, statement.Origin, "stored effects differ from the computed effects (stored effects)");
                }
            }
        }
    }

    private static void VerifyInstructionEdges(IrMethod method) {
        foreach (IrBlock block in method.Blocks) {
            ICfgNode origin = block.Terminator.Origin;
            foreach (IrEdge edge in block.Terminator.Edges) {
                if (edge.InsideInstruction) {
                    if (!ReferenceEquals(edge.Target.EntryOrigin, origin) || edge.Target.StartsInstruction) {
                        Fail(method, block, origin, "inside-instruction edge does not target a continuation of the same instruction (instruction edges)");
                    }
                } else if (!edge.Target.StartsInstruction || !origin.Successors.Contains(edge.Target.EntryOrigin)) {
                    Fail(method, block, origin, "instruction edge does not target a CFG successor's first block (instruction edges)");
                }
            }
        }
    }

    private static void VerifyEventChecks(IrMethod method) {
        foreach (IrBlock block in method.Blocks) {
            int startIndex = block.Statements.FindIndex(statement => statement.Node is InstructionStartNode);
            for (int index = 0; index < block.Statements.Count; index++) {
                if (block.Statements[index].Node is not CheckExternalEventsNode) {
                    continue;
                }
                ICfgNode origin = block.Statements[index].Origin;
                if (startIndex >= 0 && index < startIndex) {
                    continue;
                }
                if (index == block.Statements.Count - 1
                    && (block.Terminator is IrExit || !block.Terminator.Edges.Any(edge => edge.InsideInstruction))) {
                    continue;
                }
                Fail(method, block, origin, "event check is neither part of an instruction start nor the last statement before an exit (event checks)");
            }
        }
    }

    private static void VerifyFaults(IrMethod method) {
        foreach (IrBlock block in method.Blocks) {
            foreach (ICfgNode origin in block.Origins) {
                if (origin is not CfgInstruction instruction
                    || !instruction.SuccessorsPerType.TryGetValue(InstructionSuccessorType.CpuFault, out ISet<ICfgNode>? successors)) {
                    continue;
                }
                if (successors.Any(method.MethodNodes.Contains)) {
                    Fail(method, block, origin, "a CPU fault handler is inside the method (faults)");
                }
            }
        }
    }

    private static void VerifyTemporaries(IrMethod method) {
        Dictionary<string, (IrBlock Block, int Index)> declarations = [];
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                foreach (VariableDeclarationNode declaration in IrAstChildren
                        .Descendants(block.Statements[index].Node).OfType<VariableDeclarationNode>()) {
                    if (declarations.ContainsKey(declaration.VariableName)) {
                        Fail(method, block, block.Statements[index].Origin,
                            $"temporary {declaration.VariableName} is declared more than once (temporaries)");
                    }
                    declarations[declaration.VariableName] = (block, index);
                }
            }
        }
        DominatorTree<IrBlock>? dominators = null;
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index <= block.Statements.Count; index++) {
                IEnumerable<IVisitableAstNode> nodes = index < block.Statements.Count
                    ? IrAstChildren.Descendants(block.Statements[index].Node)
                    : IrAstChildren.RootsOf(block.Terminator);
                foreach (IVisitableAstNode node in nodes) {
                    if (node is not VariableReferenceNode reference
                        || node is MethodScopedVariableReferenceNode) {
                        continue;
                    }
                    if (!declarations.TryGetValue(reference.VariableName, out (IrBlock Block, int Index) declaration)) {
                        Fail(method, block, index < block.Statements.Count ? block.Statements[index].Origin : block.Terminator.Origin,
                            $"temporary {reference.VariableName} has no declaration (temporaries)");
                    }
                    if (declaration.Block == block && declaration.Index < index) {
                        continue;
                    }
                    dominators ??= DominatorTreeBuilder.BuildFromEntries(method.ToBlockGraph());
                    if (declaration.Block != block && dominators.Dominates(declaration.Block, block)) {
                        continue;
                    }
                    Fail(method, block, index < block.Statements.Count ? block.Statements[index].Origin : block.Terminator.Origin,
                        $"temporary {reference.VariableName} is read before its declaration (temporaries)");
                }
            }
        }
    }

    private static void VerifyEntries(IrMethod method) {
        foreach (IrEntry entry in method.Entries.Where(entry => !entry.Block.StartsInstruction
                || !ReferenceEquals(entry.Block.EntryOrigin, entry.Node))) {
            Fail(method, entry.Block, entry.Node, "entry block does not start its entry node (entries)");
        }
    }

    /// <summary>
    /// Verifies the SSA of <paramref name="method"/>: every use's version is defined at a point that
    /// dominates the use (an earlier statement of the same block, a phi or the entry version of a
    /// block that dominates it, or a definition on the predecessor edge of a phi operand), every phi
    /// has one operand per in-method predecessor plus the entry operand of an entry block, and every
    /// statement's and terminator's recorded uses and definitions match its stored effects.
    /// Dominance between blocks unreachable from the entries cannot be checked and is skipped.
    /// </summary>
    /// <exception cref="InvalidOperationException">A rule is violated.</exception>
    public static void VerifySsa(IrMethod method, IrSsa ssa) =>
        VerifySsa(method, ssa, SsaCheck.Strict);

    /// <summary>
    /// <see cref="VerifySsa"/> for an SSA the passes already rewrote through
    /// <see cref="IrSsa.Replace(IrStatement, IrStatement)"/> and <see cref="IrSsa.Remove"/>: the
    /// dominance and phi rules are unchanged but the bookkeeping those calls maintain is
    /// approximate in three ways this check tolerates. Recorded uses are not compared to the
    /// effects at all because Replace copies them verbatim to every replacement, so a replacement
    /// that reads more or less than the statement it replaces cannot match. A version change where
    /// the effects define nothing is tolerated when no surviving site reads the version, because
    /// Replace parks a definition no replacement writes on the last one. A partial definition whose
    /// previous version no position defines anymore keeps its recorded previous link, because a
    /// merged or removed writer leaves the link dangling.
    /// </summary>
    /// <exception cref="InvalidOperationException">A rule is violated.</exception>
    public static void VerifySsaAfterPasses(IrMethod method, IrSsa ssa) =>
        VerifySsa(method, ssa, SsaCheck.AfterPasses);

    private enum SsaCheck {
        Strict,
        AfterPasses
    }

    private static void VerifySsa(IrMethod method, IrSsa ssa, SsaCheck check) {
        BlockGraph<IrBlock> graph = method.ToBlockGraph();
        DominatorTree<IrBlock> tree = DominatorTreeBuilder.BuildFromEntries(graph);
        HashSet<IrBlock> reachable = tree.Blocks.ToHashSet();
        HashSet<IrBlock> entries = graph.EntryBlocks.ToHashSet();
        for (int version = 0; version < ssa.VersionCount; version++) {
            IrSsaVersionInfo info = ssa.Info(version);
            foreach (IrSsaUse use in ssa.Uses(version)) {
                VerifyUseDominance(method, tree, reachable, info, use);
            }
        }
        HashSet<object> liveSites = check == SsaCheck.AfterPasses ? LiveSites(method) : [];
        foreach (IrBlock block in method.Blocks) {
            VerifyPhiShapes(method, graph, entries, ssa, block);
            for (int index = 0; index <= block.Statements.Count; index++) {
                VerifySsaEffects(method, ssa, block, index, check, liveSites);
            }
        }
    }

    private static void VerifyUseDominance(IrMethod method, DominatorTree<IrBlock> tree,
        HashSet<IrBlock> reachable, IrSsaVersionInfo info, IrSsaUse use) {
        if (use.Phi is not null) {
            IrBlock predecessor = use.OperandPredecessor ?? use.Block;
            if (info.Block == predecessor) {
                return;
            }
            if (reachable.Contains(info.Block) && reachable.Contains(predecessor)
                && tree.Dominates(info.Block, predecessor)) {
                return;
            }
            if (!reachable.Contains(info.Block) || !reachable.Contains(predecessor)) {
                return;
            }
            Fail(method, use.Block, $"use of version {info.Value} by a phi operand is not dominated by its definition (ssa)");
        }
        if (info.Block == use.Block && info.StatementIndex < use.Index) {
            return;
        }
        if (info.Block != use.Block && reachable.Contains(info.Block) && reachable.Contains(use.Block)
            && tree.Dominates(info.Block, use.Block)) {
            return;
        }
        if (info.Block != use.Block && (!reachable.Contains(info.Block) || !reachable.Contains(use.Block))) {
            return;
        }
        Fail(method, use.Block, $"use of version {info.Value} is not dominated by its definition (ssa)");
    }

    private static void VerifyPhiShapes(IrMethod method, BlockGraph<IrBlock> graph,
        HashSet<IrBlock> entries, IrSsa ssa, IrBlock block) {
        List<IrBlock> predecessors = [.. graph.PredecessorsByBlock[block]];
        foreach (IrSsaPhi phi in ssa.PhisOf(block)) {
            if (phi.Operands.Count != predecessors.Count) {
                Fail(method, block, $"phi of {phi.Value} has {phi.Operands.Count} operands for {predecessors.Count} predecessors (ssa)");
            }
            if (phi.HasEntryOperand != entries.Contains(block)) {
                Fail(method, block, $"phi of {phi.Value} mismarks the entry operand (ssa)");
            }
            HashSet<IrBlock> operandPredecessors = phi.Operands.Select(operand => operand.Predecessor).ToHashSet();
            if (!operandPredecessors.SetEquals(predecessors)) {
                Fail(method, block, $"phi of {phi.Value} does not have one operand per predecessor (ssa)");
            }
        }
    }

    private static void VerifySsaEffects(IrMethod method, IrSsa ssa, IrBlock block, int index,
        SsaCheck check, HashSet<object> liveSites) {
        bool isTerminator = index == block.Statements.Count;
        IrEffects effects = isTerminator ? block.Terminator.Effects : block.Statements[index].Effects;
        (uint full, uint partial) = IrSsaValues.WriteBits(effects);
        uint used = IrSsaValues.ValueBits(effects.Reads) | IrSsaValues.TokenBits(effects.TokenReads)
            | (effects.MayFault ? IrSsaValues.All : 0) | partial;
        for (int v = 0; v < (int)IrSsaValue.Count; v++) {
            IrSsaValue value = (IrSsaValue)v;
            if (check == SsaCheck.Strict) {
                int expectedUsed = (used & (1u << v)) != 0 ? 1 : 0;
                int actualUsed = VersionUsed(ssa, block, index, isTerminator, value) != -1 ? 1 : 0;
                if (expectedUsed != actualUsed) {
                    Fail(method, block, $"use record of {value} does not match the effects (ssa)");
                }
            }
            int before = ssa.VersionBefore(block, index, value);
            int after = ssa.VersionAfter(block, index, value);
            if ((full & (1u << v)) != 0) {
                if (ssa.Info(after).Kind != IrSsaDefinitionKind.Definition) {
                    Fail(method, block, $"full definition of {value} is not recorded as one (ssa)");
                }
            } else if ((partial & (1u << v)) != 0) {
                // After the passes, a merged or removed writer leaves its version as the previous
                // link of a partial definition: the link is stale once no position defines it.
                bool previousOrphaned = check == SsaCheck.AfterPasses
                    && ssa.Info(after).Previous >= 0 && ssa.Info(after).Previous != before
                    && !ssa.HasDefinitionSite(value, ssa.Info(after).Previous);
                if (ssa.Info(after).Kind != IrSsaDefinitionKind.PartialDefinition
                    || (ssa.Info(after).Previous != before && !previousOrphaned)) {
                    Fail(method, block, $"partial definition of {value} is not recorded as one (ssa)");
                }
            } else if (after != before
                && (check == SsaCheck.Strict || HasLiveUse(ssa, after, liveSites))) {
                // After the passes, a version change where the effects define nothing is tolerated
                // when no surviving site reads it: Replace parks the definition on the last
                // replacement when none writes the value.
                Fail(method, block, $"version of {value} changes where the effects define nothing (ssa)");
            }
        }
    }

    private static int VersionUsed(IrSsa ssa, IrBlock block, int index, bool isTerminator,
        IrSsaValue value) =>
        isTerminator
            ? ssa.VersionUsed(block.Terminator, value)
            : ssa.VersionUsed(block.Statements[index], value);

    /// <summary>The statements and terminators <paramref name="method"/> currently holds: the site
    /// table keeps entries for objects a pass replaced or removed, so only objects still reachable
    /// from the blocks count.</summary>
    private static HashSet<object> LiveSites(IrMethod method) {
        HashSet<object> sites = new(ReferenceEqualityComparer.Instance);
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                sites.Add(statement);
            }
            sites.Add(block.Terminator);
        }
        return sites;
    }

    /// <summary>Whether a surviving site still reads <paramref name="version"/>: the liveness
    /// recorded at build goes stale when a pass replaces or removes the only statement or terminator
    /// that used it, so each live use must map back to a site the method still holds.</summary>
    private static bool HasLiveUse(IrSsa ssa, int version, HashSet<object> liveSites) =>
        ssa.LiveUses(version).Any(use => use.Phi is not null
            || liveSites.Contains((object?)use.Statement ?? use.Terminator!));

    private static bool IsForbiddenInIr(IVisitableAstNode node) => node is
        IfElseNode or WhileNode or WhileWithExitNode or BlockNode or GotoNode or LabelNode or BreakNode
        or ContinueNode or SwitchNode or InfiniteLoopNode or DoWhileNode or CpuFaultTryCatchNode
        or InstructionBoundaryNode or EntryDispatcherLabelNode or InstructionNode or JumpNearNode
        or JumpFarNode or CallNearNode or CallFarNode or MoveIpNextNode or HltNode or SelectorNode
        or InvalidInstructionNode or CpuidNode or InterruptCallNode or ReturnNearNode or ReturnFarNode
        or ReturnInterruptNode or CallbackNode;

    private static void Fail(IrMethod method, IrBlock block, string rule) =>
        throw new InvalidOperationException($"IR of {method.Name}, block {block.Id}: {rule}.");

    private static void Fail(IrMethod method, IrBlock block, ICfgNode origin, string rule) =>
        throw new InvalidOperationException($"IR of {method.Name}, block {block.Id} at {origin.Address}: {rule}.");
}
