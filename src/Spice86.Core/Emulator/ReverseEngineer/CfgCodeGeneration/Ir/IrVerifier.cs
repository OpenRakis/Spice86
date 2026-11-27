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
            if (IrAstChildren.TerminatorNodes(block.Terminator).OfType<ConditionalValueNode>()
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

    /// <summary>Whether <paramref name="node"/> evaluates without side effects: constants, registers,
    /// flags, variables and operations on them, but no memory read, helper call or anything that can
    /// fault. Shared with <see cref="Passes.IrIfConversion"/>, which applies the same rule to the values it
    /// moves into a conditional arm.</summary>
    internal static bool IsPureArm(ValueNode node) =>
        IrAstChildren.Descendants(node).All(IsPureArmNode);

    private static bool IsPureArmNode(IVisitableAstNode node) => node switch {
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN or BinaryOperation.DIVIDE
            or BinaryOperation.MODULO } => false,
        ConstantNode or RegisterNode or SegmentRegisterNode or CpuFlagNode or FlagRegisterNode
            or VariableReferenceNode or UnaryOperationNode or BinaryOperationNode
            or TypeConversionNode or ExplicitCastNode or ConditionalValueNode or NearAddressNode
            or InstructionFieldNode or RegisterLocalNode => true,
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
                    IrStatement.MayFault(statement.Node, statement.Origin))
                    with { FaultReads = statement.Effects.FaultReads };
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
            int startIndex = -1;
            for (int i = 0; i < block.Statements.Count; i++) {
                if (block.Statements[i].Node is InstructionStartNode) {
                    startIndex = i;
                    break;
                }
            }
            for (int index = 0; index < block.Statements.Count; index++) {
                if (block.Statements[index].Node is not CheckExternalEventsNode) {
                    continue;
                }
                ICfgNode origin = block.Statements[index].Origin;
                if (startIndex >= 0 && index < startIndex) {
                    continue;
                }
                // A check an interrupt shadow moves after an instruction sits between
                // instructions, right before the next instruction's start or its speculative guard.
                if (index + 1 < block.Statements.Count
                    && block.Statements[index + 1].Node is InstructionStartNode or SpeculativeGuardNode) {
                    continue;
                }
                // A check may also close a loop whose edges stay inside one instruction: the HLT wait loop
                // re-enters at its own head, so the check sits at the loop latch and re-entry is clean.
                if (index == block.Statements.Count - 1
                    && (block.Terminator is IrExit
                        || !block.Terminator.Edges.Any(edge => edge.InsideInstruction)
                        || block.Terminator.Edges.All(edge => edge.InsideInstruction))) {
                    continue;
                }
                Fail(method, block, origin, "event check is neither part of an instruction start, the last statement of an instruction, the last statement before an exit, nor at an instruction-internal loop latch (event checks)");
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
                IEnumerable<IVisitableAstNode> nodes = IrAstChildren.TerminatorNodes(block.Terminator);
                if (index < block.Statements.Count) {
                    nodes = IrAstChildren.Descendants(block.Statements[index].Node);
                }
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
    public static void VerifySsa(IrMethod method, IrSsa ssa) {
        BlockGraph<IrBlock> graph = method.ToBlockGraph();
        DominatorTree<IrBlock> tree = DominatorTreeBuilder.BuildFromEntries(graph);
        HashSet<IrBlock> reachable = tree.Blocks.ToHashSet();
        HashSet<IrBlock> entries = graph.EntryBlocks.ToHashSet();
        for (int version = 0; version < ssa.VersionCount; version++) {
            foreach (IrSsaUse use in ssa.Uses(version)) {
                VerifyUseDominance(method, ssa, tree, reachable, version, use);
            }
        }
        foreach (IrBlock block in method.Blocks) {
            VerifyPhiShapes(method, graph, entries, ssa, block);
            for (int index = 0; index <= block.Statements.Count; index++) {
                VerifySsaEffects(method, ssa, block, index);
            }
        }
    }

    private static void VerifyUseDominance(IrMethod method, IrSsa ssa, DominatorTree<IrBlock> tree,
        HashSet<IrBlock> reachable, int version, IrSsaUse use) {
        IrSsaVersionInfo info = ssa.Info(version);
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
        object? useSite = use.Statement ?? (object?)use.Terminator;
        int useIndex = useSite is null ? -1 : IrSsa.PositionIn(use.Block, useSite);
        object? definitionSite = ssa.DefinitionSite(version);
        int definitionIndex = definitionSite is null
            ? -1
            : IrSsa.PositionIn(info.Block, definitionSite);
        if (info.Block == use.Block && definitionIndex >= 0 && definitionIndex < useIndex) {
            return;
        }
        if (info.Block == use.Block && definitionIndex < 0) {
            // A phi or a method entry version counts as defined at the block's entry.
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

    private static void VerifySsaEffects(IrMethod method, IrSsa ssa, IrBlock block, int index) {
        bool isTerminator = index == block.Statements.Count;
        IrEffects effects = isTerminator ? block.Terminator.Effects : block.Statements[index].Effects;
        (uint full, uint partial) = IrSsaValues.WriteBits(effects);
        uint used = IrSsa.UsedBits(effects);
        for (int v = 0; v < (int)IrSsaValue.Count; v++) {
            IrSsaValue value = (IrSsaValue)v;
            int expectedUsed = (used & (1u << v)) != 0 ? 1 : 0;
            int actualUsed = VersionUsed(ssa, block, index, isTerminator, value) != -1 ? 1 : 0;
            if (expectedUsed != actualUsed) {
                Fail(method, block, $"use record of {value} does not match the effects (ssa)");
            }
            int before = ssa.VersionBefore(block, index, value);
            int after = ssa.VersionAfter(block, index, value);
            if ((full & (1u << v)) != 0) {
                if (ssa.Info(after).Kind != IrSsaDefinitionKind.Definition) {
                    Fail(method, block, $"full definition of {value} is not recorded as one (ssa)");
                }
            } else if ((partial & (1u << v)) != 0) {
                if (ssa.Info(after).Kind != IrSsaDefinitionKind.PartialDefinition
                    || ssa.Info(after).Previous != before) {
                    Fail(method, block, $"partial definition of {value} is not recorded as one (ssa)");
                }
            } else if (after != before) {
                Fail(method, block, $"version of {value} changes where the effects define nothing (ssa)");
            }
        }
    }

    private static int VersionUsed(IrSsa ssa, IrBlock block, int index, bool isTerminator,
        IrSsaValue value) =>
        isTerminator
            ? ssa.VersionUsed(block.Terminator, value)
            : ssa.VersionUsed(block.Statements[index], value);

    private static bool IsForbiddenInIr(IVisitableAstNode node) => node is
        IfElseNode or WhileNode or WhileWithExitNode or BlockNode or GotoNode or LabelNode or BreakNode
        or ContinueNode or SwitchNode or InfiniteLoopNode or DoWhileNode or CpuFaultTryCatchNode
        or InstructionBoundaryNode or EntryDispatcherLabelNode or InstructionNode or JumpNearNode
        or JumpFarNode or CallNearNode or CallFarNode or MoveIpNextNode or HltNode or SelectorNode
        or InvalidInstructionNode or CpuidNode or InterruptCallNode or ReturnNearNode or ReturnFarNode
        or ReturnInterruptNode or CallbackNode;

    private static void Fail(IrMethod method, IrBlock block, string rule) =>
        throw new InvalidOperationException($"IR of {method.Name}, block {block.Id}: {rule}.");

    /// <summary>The identity of one SSA version across two builds of the same method: its value, its
    /// kind and the site or block that defines it.</summary>
    private sealed record SsaVersionKey(IrSsaValue Value, IrSsaDefinitionKind Kind, IrBlock Block,
        object? Site);

    /// <summary>Builds the SSA of <paramref name="method"/> again and throws when
    /// <paramref name="maintained"/> differs from it: every site must record the same uses and
    /// definitions, and every version that exists under either build must hold the same live use
    /// sites. Versions are compared by what they are - value, kind and defining site - never by id.</summary>
    /// <exception cref="InvalidOperationException">The maintained SSA diverged from a fresh build.</exception>
    internal static void VerifySsaMatchesFreshBuild(IrMethod method, IrSsa maintained) {
        if (maintained.IsLivenessStale) {
            maintained.RecomputeLiveness();
        }
        IrSsa fresh = IrSsa.Build(method);
        List<string> differences = [];
        CompareSiteRecords(maintained, fresh, differences);
        CompareLiveUses(maintained, fresh, differences);
        if (differences.Count != 0) {
            throw new InvalidOperationException(
                $"The maintained SSA of {method.Name} does not match a fresh build:\n"
                + string.Join('\n', differences.Take(20)));
        }
    }

    private static void CompareSiteRecords(IrSsa maintained, IrSsa fresh, List<string> differences) {
        HashSet<object> sites = new(ReferenceEqualityComparer.Instance);
        foreach (object site in maintained.UsesBySite.Keys.Concat(fresh.UsesBySite.Keys)
                .Concat(maintained.DefinitionsBySite.Keys).Concat(fresh.DefinitionsBySite.Keys)) {
            sites.Add(site);
        }
        foreach (object site in sites) {
            ComparePairs(site, "uses", PairsOf(maintained.UsesBySite, site, maintained),
                PairsOf(fresh.UsesBySite, site, fresh), differences);
            ComparePairs(site, "definitions", PairsOf(maintained.DefinitionsBySite, site, maintained),
                PairsOf(fresh.DefinitionsBySite, site, fresh), differences);
        }
    }

    private static HashSet<(IrSsaValue Value, SsaVersionKey Key)> PairsOf(
        IReadOnlyDictionary<object, (IrSsaValue Value, int Version)[]> records, object site, IrSsa ssa) {
        if (!records.TryGetValue(site, out (IrSsaValue Value, int Version)[]? pairs)) {
            return [];
        }
        HashSet<(IrSsaValue Value, SsaVersionKey Key)> keys = [];
        foreach ((IrSsaValue value, int version) in pairs) {
            keys.Add((value, VersionKeyOf(ssa, version)));
        }
        return keys;
    }

    private static void ComparePairs(object site, string label,
        HashSet<(IrSsaValue Value, SsaVersionKey Key)> maintained,
        HashSet<(IrSsaValue Value, SsaVersionKey Key)> fresh, List<string> differences) {
        if (maintained.SetEquals(fresh)) {
            return;
        }
        differences.Add($"{DescribeSite(site)} {label}: "
            + $"maintained [{string.Join(", ", maintained)}] fresh [{string.Join(", ", fresh)}]");
    }

    private static void CompareLiveUses(IrSsa maintained, IrSsa fresh, List<string> differences) {
        Dictionary<SsaVersionKey, HashSet<object>> maintainedLive = LiveUseSites(maintained);
        Dictionary<SsaVersionKey, HashSet<object>> freshLive = LiveUseSites(fresh);
        foreach (SsaVersionKey key in maintainedLive.Keys.Concat(freshLive.Keys).Distinct()) {
            HashSet<object> maintainedSites = maintainedLive.GetValueOrDefault(key, []);
            HashSet<object> freshSites = freshLive.GetValueOrDefault(key, []);
            if (maintainedSites.SetEquals(freshSites)) {
                continue;
            }
            differences.Add($"live uses of {key}: maintained [{SitesOf(maintainedSites)}] "
                + $"fresh [{SitesOf(freshSites)}]");
        }
    }

    private static Dictionary<SsaVersionKey, HashSet<object>> LiveUseSites(IrSsa ssa) {
        Dictionary<SsaVersionKey, HashSet<object>> live = new();
        for (int version = 0; version < ssa.VersionCount; version++) {
            SsaVersionKey key = VersionKeyOf(ssa, version);
            HashSet<object> sites = [];
            foreach (IrSsaUse use in ssa.LiveUses(version)) {
                sites.Add(UseSiteKey(use));
            }
            live[key] = sites;
        }
        return live;
    }

    private static SsaVersionKey VersionKeyOf(IrSsa ssa, int version) {
        IrSsaVersionInfo info = ssa.Info(version);
        object? site = info.Kind is IrSsaDefinitionKind.Definition or IrSsaDefinitionKind.PartialDefinition
            ? ssa.DefinitionSite(version)
            : null;
        return new SsaVersionKey(info.Value, info.Kind, info.Block, site);
    }

    private static object UseSiteKey(IrSsaUse use) {
        if (use.Phi is { } phi) {
            return (phi.Value, use.Block, use.OperandPredecessor);
        }
        return (object?)use.Statement ?? use.Terminator
            ?? throw new InvalidOperationException("An SSA use holds no site.");
    }

    private static string SitesOf(IEnumerable<object> sites) =>
        string.Join(", ", sites.Select(DescribeSite));

    private static string DescribeSite(object site) => site switch {
        IrStatement statement => $"statement {statement.Node.GetType().Name} at {statement.Origin.Address}",
        IrTerminator terminator => $"terminator {terminator.GetType().Name} at {terminator.Origin.Address}",
        _ => site.ToString() ?? "site"
    };

    private static void Fail(IrMethod method, IrBlock block, ICfgNode origin, string rule) =>
        throw new InvalidOperationException($"IR of {method.Name}, block {block.Id} at {origin.Address}: {rule}.");
}
