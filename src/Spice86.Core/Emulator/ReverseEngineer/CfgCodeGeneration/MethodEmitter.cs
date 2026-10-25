namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Conditions;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Liveness;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

using CfgSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>
/// Drives the emission of one C# method per CFG partition. It builds one AST per method out of the plan's
/// ordered list of nodes, then prints it with <see cref="CSharpStatementPrinter"/>. The AST carries the method
/// skeleton too: entry dispatch switch, block labels and the entry dispatcher label.
/// </summary>
internal sealed class MethodEmitter(
    CfgGeneratorContext context,
    CpuFaultWrapper cpuFaultWrapper,
    InstructionLowerer lowerer,
    AstInstructionRenderer assemblyRenderer) {

    private readonly CSharpExpressionPrinter _expressions = new(context.SegmentVariables);

    /// <returns>The census of the <c>goto</c>s the printed method holds.</returns>
    public GotoCensus Emit(CSharpSourceWriter writer, MethodPlan method) {
        FlagLiveness liveness = FlagLiveness.Compute(method);
        ConditionFusion fusion = ConditionFusionPlanner.Plan(method, liveness);
        // Fused jumps no longer read flags, so the lowering uses the liveness of the code it actually emits.
        FlagLiveness emittedLiveness = FlagLiveness.Compute(method, fusion.ExecutionAstOf);
        BlockNode entryDispatch = BuildEntryDispatch(method);
        List<InstructionBoundaryNode> boundaries = method.NodeEmissionPlans
            .Select(plan => BuildBoundary(plan, method, fusion, emittedLiveness)).ToList();
        BlockGraphGotoCheck.Check(method.NodeEmissionPlans, boundaries, method.BlockGraph, method.MethodName);
        NaturalLoopForest loops = NaturalLoopForest.Build(method.BlockGraph, method.BlockTraversal.RetreatingEdges);
        HashSet<ICfgNode> entryNodes = method.Entries.Select(entry => entry.Node).ToHashSet();
        List<LoweredBlock> loweredBlocks = GroupByBlock(method.NodeEmissionPlans, boundaries);
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);
        BlockNode withoutFallthroughGotos = FallthroughGotoRemover.Remove(
            MethodStructurizer.Structure(loweredBlocks, loops, entryNodes, method.MethodName, refusals, placeLoopExits: false));
        Dictionary<GotoNode, GotoKeepReason> placedRefusals = new(ReferenceEqualityComparer.Instance);
        BlockNode placedWithoutFallthroughGotos = FallthroughGotoRemover.Remove(
            MethodStructurizer.Structure(loweredBlocks, loops, entryNodes, method.MethodName, placedRefusals, placeLoopExits: true));
        if (MethodStructurizer.TotalGotos(placedWithoutFallthroughGotos) < MethodStructurizer.TotalGotos(withoutFallthroughGotos)) {
            withoutFallthroughGotos = placedWithoutFallthroughGotos;
            refusals = placedRefusals;
        }
        withoutFallthroughGotos = TailDuplicator.Apply(withoutFallthroughGotos, loweredBlocks, entryNodes, loops);
        GotoCensus census = GotoCensusClassifier.Classify(entryDispatch, withoutFallthroughGotos, loops, entryNodes,
            loweredBlocks, refusals);
        GotoTargets targets = GotoTargetCollector.Collect(Sequence(entryDispatch, withoutFallthroughGotos));
        BlockNode body = LabelPlacer.Place(withoutFallthroughGotos, targets.Labels);
        LabelScopeCheck.Verify(Sequence(entryDispatch, body));
        // The last node has no next node, so every same-method fallthrough from it is a goto, and an unobserved
        // fallthrough is a throw: a body that can still fall off its end is a generator bug.
        if (CompletesNormallyVisitor.CompletesNormally(body)) {
            throw new InvalidOperationException($"Generated method {method.MethodName} can fall off its end: its last node does not diverge.");
        }
        writer.OpenBlock($"public virtual Action {method.MethodName}(int loadOffset)");
        CSharpStatementPrinter printer = new(writer, _expressions, method.GetLabel);
        if (targets.EntryDispatcher) {
            printer.Print(new EntryDispatcherLabelNode());
        }
        printer.Print(entryDispatch);
        if (!IsEmpty(entryDispatch)) {
            writer.Line();
        }
        printer.Print(body);
        writer.CloseBlock();
        writer.Line();
        return census;
    }

    /// <summary>Groups contiguous boundaries sharing one CFG block into <see cref="LoweredBlock"/>s.</summary>
    private static List<LoweredBlock> GroupByBlock(IReadOnlyList<NodeEmissionPlan> plans, IReadOnlyList<InstructionBoundaryNode> boundaries) {
        List<LoweredBlock> blocks = [];
        int start = 0;
        while (start < plans.Count) {
            int end = start + 1;
            while (end < plans.Count && ReferenceEquals(plans[end].Block, plans[start].Block)) {
                end++;
            }
            BlockNode body = Sequence(boundaries.Skip(start).Take(end - start));
            blocks.Add(new LoweredBlock(plans[start].Block, plans[start].EmitsExternalEventCheck, body));
            start = end;
        }
        return blocks;
    }

    private BlockNode BuildEntryDispatch(MethodPlan method) {
        if (method.NeedsEntryDispatch) {
            List<SwitchSection> sections = [];
            foreach (CfgCodePartitionEntry entry in method.Entries) {
                List<IVisitableAstNode> entryBody = [];
                if (!entry.Node.Equals(entry.Block.Entry)) {
                    entryBody.Add(new CheckExternalEventsNode(entry.Address));
                }
                entryBody.Add(new GotoNode(entry.Node));
                sections.Add(Section(context.GetEntryLoadOffset(method.Partition, entry.Node), Sequence(entryBody)));
            }
            return Sequence(new SwitchNode(new LoadOffsetParameterNode(), sections, Sequence(UntestedMessages.EntryOffset())));
        }

        // Single entry: the block layout starts with the primary entry block, so no jump is needed.
        ICfgNode primaryEntry = method.PrimaryEntry.Node;
        if (!method.NodeEmissionPlans[0].Node.Equals(primaryEntry)) {
            throw new InvalidOperationException($"Primary entry {primaryEntry.Address} of {method.MethodName} is not the first emitted node.");
        }
        return Empty();
    }

    private InstructionBoundaryNode BuildBoundary(NodeEmissionPlan plan, MethodPlan method, ConditionFusion fusion,
        FlagLiveness liveness) {
        IVisitableAstNode eventCheck = EntryEventCheck(plan);
        IVisitableAstNode guard = SpeculativeGuard(plan.Node);
        IVisitableAstNode comment = AsmComment(plan.Node);
        IVisitableAstNode body;
        string suffix;
        if (plan.Node is CfgInstruction instruction) {
            suffix = method.GetLocalSuffix(instruction);
            body = cpuFaultWrapper.Wrap(instruction,
                lowerer.LowerInstructionBody(instruction, fusion.ExecutionAstOf(instruction), plan.EmitsExternalEventCheckAfter, liveness.LiveAfter(instruction)), method);
        } else if (plan.Node is CfgSelectorNode selectorNode) {
            // Uniform Accept dispatch: the selector's ExecutionAst is the AST SelectorNode marker, whose
            // Accept routes to VisitSelectorNode. A selector is always a block terminator, so it never has
            // a fallthrough to append (unlike an instruction body).
            suffix = string.Empty;
            body = selectorNode.ExecutionAst.Accept(lowerer);
        } else {
            throw new NotSupportedException($"CFG C# generation does not support node {plan.Node.GetType().FullName} yet at {plan.Node.Address}.");
        }
        return new InstructionBoundaryNode(plan.Node, suffix, Sequence(eventCheck, guard, comment, body));
    }

    private IVisitableAstNode EntryEventCheck(NodeEmissionPlan plan) {
        if (!plan.EmitsExternalEventCheck) {
            return Empty();
        }

        // Checks run at method entries and loop back-edge targets, so every execution path that
        // repeats passes one. Anchoring to the node keeps the expected resume point aligned with its label;
        // for block entries the node is the block entry, so the emitted check is unchanged.
        ICfgNode entry = plan.Node;
        return new CheckExternalEventsNode(entry.Address);
    }

    /// <summary>
    /// Builds a <c>VerifySpeculativeEntryOrFail</c> guard for a single speculative instruction.
    /// </summary>
    /// <remarks>
    /// The guard re-reads the instruction's bytes from memory immediately before its body executes and fails as
    /// untested if they no longer match the signature decoded at exploration time. Emitting one guard per
    /// speculative instruction (rather than a single block-entry guard covering the whole run) is what lets
    /// the generated code detect self-modifying code that an earlier instruction in the same block performs
    /// against a later speculative instruction: an entry-only guard runs before any instruction executes and
    /// so cannot observe such a mutation.
    /// </remarks>
    private IVisitableAstNode SpeculativeGuard(ICfgNode node) {
        if (node is not CfgInstruction { IsSpeculative: true } speculativeInstruction) {
            return Empty();
        }

        IReadOnlyList<byte?> signatureValue = speculativeInstruction.Signature.SignatureValue;
        if (signatureValue.Count == 0) {
            return Empty();
        }
        string fieldName = context.GetSignatureField(speculativeInstruction);
        return new SpeculativeGuardNode(speculativeInstruction.Address, fieldName);
    }

    /// <summary>Builds the source comment that identifies the instruction or selector node being emitted.</summary>
    private IVisitableAstNode AsmComment(ICfgNode node) {
        switch (node) {
            case CfgInstruction instruction:
                return new CommentNode($"{instruction.Address} {instruction.DisplayAst.Accept(assemblyRenderer)}");
            case CfgSelectorNode selectorNode:
                return new CommentNode($"{selectorNode.Address} selector");
            default:
                return Empty();
        }
    }
}
