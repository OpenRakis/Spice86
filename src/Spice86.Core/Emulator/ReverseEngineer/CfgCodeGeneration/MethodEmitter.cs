namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

using CfgSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>
/// Drives the emission of one C# method per CFG partition. It walks the plan's ordered list of nodes,
/// asks the AST emitter to lower each node's body, wraps it with fault handling if needed, then hands
/// the result to the renderer. Also emits the method skeleton: signature, entry dispatch switch,
/// block labels, and the trailing safety-net throw.
/// </summary>
internal sealed class MethodEmitter(
    CfgGeneratorContext context,
    CpuFaultWrapper cpuFaultWrapper,
    CSharpAstEmitter astEmitter,
    AstInstructionRenderer assemblyRenderer) {

    public void Emit(CSharpSourceWriter writer, MethodPlan method) {
        astEmitter.SetCurrentMethod(method);
        EmittedCode entryDispatch = EmitEntryDispatch(method);
        List<LoweredNode> loweredNodes = method.NodeEmissionPlans.Select(plan => Lower(plan, method)).ToList();

        List<StatementItem> allStatements = StatementWalker
            .Descendants(entryDispatch.AsStatements())
            .Concat(loweredNodes.SelectMany(lowered => StatementWalker.Descendants(lowered.Code.AsStatements())))
            .ToList();
        HashSet<ICfgNode> gotoTargets = allStatements.OfType<GotoStatement>().Select(gotoStatement => gotoStatement.Target).ToHashSet();
        bool needsEntryDispatcherLabel = allStatements.OfType<GotoEntryDispatcherStatement>().Any();

        EmittedCodeRenderer renderer = new(context.GetLabel);
        writer.OpenBlock($"public virtual Action {method.MethodName}(int loadOffset)");
        if (needsEntryDispatcherLabel) {
            writer.Label("entrydispatcher");
        }
        renderer.Render(entryDispatch, writer);
        if (!entryDispatch.IsEmpty) {
            writer.Line();
        }
        bool bodyCompletesNormally = true;
        foreach (LoweredNode lowered in loweredNodes) {
            if (lowered.Plan.IsBlockEntry && gotoTargets.Contains(lowered.Plan.Node)) {
                writer.Label(lowered.Plan.Label);
            }
            renderer.Render(lowered.Code, writer);
            bodyCompletesNormally = lowered.Code.CompletesNormally;
        }
        // The trailing untested-failure throw is a real safety net only when control can fall off the end of
        // the body. A body whose last node diverges (ret/hlt/goto/partition-return/throw) never reaches it, so
        // emitting it there would be dead code. Completion is read from the emitted-code structure, not by
        // re-parsing generated text.
        if (bodyCompletesNormally) {
            writer.Line("throw FailAsUntested(\"Generated partition reached the end without a terminating control-flow instruction.\");");
        }
        writer.CloseBlock();
        writer.Line();
    }

    private sealed record LoweredNode(NodeEmissionPlan Plan, EmittedCode Code);

    private EmittedCode EmitEntryDispatch(MethodPlan method) {
        if (method.NeedsEntryDispatch) {
            List<SwitchCase> cases = [];
            List<StatementItem> defaultBody = [new LineStatement("throw FailAsUntested($\"Unknown generated entry loadOffset 0x{loadOffset:X4}\");", Diverges: true)];
            foreach (CfgCodePartitionEntry entry in method.Entries) {
                cases.Add(new SwitchCase($"0x{context.GetEntryLoadOffset(method.Partition, entry.Node):X4}", [new GotoStatement(entry.Node)]));
            }
            return EmittedCode.Statements(new SwitchStatement("switch (loadOffset)", cases, defaultBody));
        }

        // Single entry: only jump when the entry is not already the first node emitted in the body
        // (the entry point can be a reset vector at a higher address than the first emitted block).
        ICfgNode primaryEntry = method.PrimaryEntry.Node;
        if (method.NodeEmissionPlans.Count > 0 && method.NodeEmissionPlans[0].Node.Equals(primaryEntry)) {
            return EmittedCode.None;
        }
        return EmittedCode.Statements(new GotoStatement(primaryEntry));
    }

    private LoweredNode Lower(NodeEmissionPlan plan, MethodPlan method) {
        EmittedCode eventCheck = BlockEntryEventCheck(plan);
        EmittedCode speculativeGuard = SpeculativeGuard(plan.Node);
        EmittedCode assemblyComment = AsmComment(plan.Node);
        EmittedCode body = BuildNodeBody(plan.Node, method);
        return new LoweredNode(plan, EmittedCode.Concat(eventCheck, speculativeGuard, assemblyComment, body));
    }

    private EmittedCode BlockEntryEventCheck(NodeEmissionPlan plan) {
        if (!plan.IsBlockEntry) {
            return EmittedCode.None;
        }

        // One external-event check per block, anchored to the block entry node's segmented address.
        // A block is the unit of straight-line execution between control-flow boundaries, so a single
        // check at block entry is sufficient: once entered, execution runs to the terminator without an
        // intervening external-event boundary. Anchoring to the block entry keeps the expected resume
        // point aligned with the label other transfers goto, so a handler returning into the middle of a
        // block is still rejected.
        ICfgNode entry = plan.Block.Entry;
        return EmittedCode.Line($"CheckExternalEvents({context.GetSegmentVariable(entry.Address.Segment)}, 0x{entry.Address.Offset:X4});");
    }

    /// <summary>
    /// Emits a <c>VerifySpeculativeEntryOrFail</c> guard for a single speculative instruction.
    /// </summary>
    /// <remarks>
    /// The guard re-reads the instruction's bytes from memory immediately before its body executes and fails as
    /// untested if they no longer match the signature decoded at exploration time. Emitting one guard per
    /// speculative instruction (rather than a single block-entry guard covering the whole run) is what lets
    /// the generated code detect self-modifying code that an earlier instruction in the same block performs
    /// against a later speculative instruction: an entry-only guard runs before any instruction executes and
    /// so cannot observe such a mutation.
    /// </remarks>
    private EmittedCode SpeculativeGuard(ICfgNode node) {
        if (node is not CfgInstruction { IsSpeculative: true } speculativeInstruction) {
            return EmittedCode.None;
        }

        IReadOnlyList<byte?> signatureValue = speculativeInstruction.Signature.SignatureValue;
        if (signatureValue.Count == 0) {
            return EmittedCode.None;
        }
        string signatureBytes = string.Join(", ", signatureValue.Select(value => value is byte byteValue ? $"(byte)0x{byteValue:X2}" : "null"));
        string segmentVariable = context.GetSegmentVariable(speculativeInstruction.Address.Segment);
        return EmittedCode.Line($"VerifySpeculativeEntryOrFail({segmentVariable}, 0x{speculativeInstruction.Address.Offset:X4}, [{signatureBytes}]);");
    }

    /// <summary>Creates the source comment that identifies the instruction or selector node being emitted.</summary>
    private EmittedCode AsmComment(ICfgNode node) {
        switch (node) {
            case CfgInstruction instruction:
                return EmittedCode.Line($"// {instruction.Address} {instruction.DisplayAst.Accept(assemblyRenderer)}");
            case CfgSelectorNode selectorNode:
                return EmittedCode.Line($"// {selectorNode.Address} selector");
            default:
                return EmittedCode.None;
        }
    }

    private EmittedCode BuildNodeBody(ICfgNode node, MethodPlan method) {
        switch (node) {
            case CfgInstruction instruction:
                astEmitter.SetCurrentInstructionAddress(instruction.Address);
                EmittedCode body = astEmitter.LowerInstructionBody(instruction, instruction.ExecutionAst);
                return cpuFaultWrapper.Wrap(instruction, body, method);
            case CfgSelectorNode selectorNode:
                // Uniform Accept dispatch: the selector's ExecutionAst is the AST SelectorNode marker, whose
                // Accept routes to VisitSelectorNode. A selector is always a block terminator, so it never has
                // a fallthrough to append (unlike an instruction body).
                return selectorNode.ExecutionAst.Accept(astEmitter);
            default:
                throw new NotSupportedException($"CFG C# generation does not support node {node.GetType().FullName} yet at {node.Address}.");
        }
    }

}
