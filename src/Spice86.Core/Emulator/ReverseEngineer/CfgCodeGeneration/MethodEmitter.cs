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
/// the result to the renderer. Also emits the method skeleton: signature, entry dispatch switch
/// and block labels.
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

        EmittedCodeRenderer renderer = new(method.GetLabel);
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
            if (gotoTargets.Contains(lowered.Plan.Node)) {
                writer.Label(method.GetLabel(lowered.Plan.Node));
            }
            renderer.Render(lowered.Code, writer);
            bodyCompletesNormally = lowered.Code.CompletesNormally;
        }
        // The last node has no next node, so every same-method fallthrough from it is a goto, and an unobserved
        // fallthrough is a throw: a body that can still fall off its end is a generator bug.
        if (bodyCompletesNormally) {
            throw new InvalidOperationException($"Generated method {method.MethodName} can fall off its end: its last node does not diverge.");
        }
        writer.CloseBlock();
        writer.Line();
    }

    private sealed record LoweredNode(NodeEmissionPlan Plan, EmittedCode Code);

    private EmittedCode EmitEntryDispatch(MethodPlan method) {
        if (method.NeedsEntryDispatch) {
            List<SwitchCase> cases = [];
            List<StatementItem> defaultBody = [new LineStatement(UntestedMessages.EntryOffset(), Diverges: true)];
            foreach (CfgCodePartitionEntry entry in method.Entries) {
                List<StatementItem> body = [];
                if (!entry.Node.Equals(entry.Block.Entry)) {
                    body.Add(new LineStatement($"CheckExternalEvents({context.GetSegmentVariable(entry.Address.Segment)}, 0x{entry.Address.Offset:X4});"));
                }
                body.Add(new GotoStatement(entry.Node));
                cases.Add(new SwitchCase($"0x{context.GetEntryLoadOffset(method.Partition, entry.Node):X4}", body));
            }
            return EmittedCode.Statements(new SwitchStatement("switch (loadOffset)", cases, defaultBody));
        }

        // Single entry: the block layout starts with the primary entry block, so no jump is needed.
        ICfgNode primaryEntry = method.PrimaryEntry.Node;
        if (!method.NodeEmissionPlans[0].Node.Equals(primaryEntry)) {
            throw new InvalidOperationException($"Primary entry {primaryEntry.Address} of {method.MethodName} is not the first emitted node.");
        }
        return EmittedCode.None;
    }

    private LoweredNode Lower(NodeEmissionPlan plan, MethodPlan method) {
        EmittedCode eventCheck = EntryEventCheck(plan);
        EmittedCode speculativeGuard = SpeculativeGuard(plan.Node);
        EmittedCode assemblyComment = AsmComment(plan.Node);
        EmittedCode body = BuildNodeBody(plan, method);
        return new LoweredNode(plan, EmittedCode.Concat(eventCheck, speculativeGuard, assemblyComment, body));
    }

    private EmittedCode EntryEventCheck(NodeEmissionPlan plan) {
        if (!plan.EmitsExternalEventCheck) {
            return EmittedCode.None;
        }

        // Checks run at method entries and loop back-edge targets, so every execution path that
        // repeats passes one. Anchoring to the node keeps the expected resume point aligned with its label;
        // for block entries the node is the block entry, so the emitted check is unchanged.
        ICfgNode entry = plan.Node;
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
        string fieldName = context.GetSignatureField(speculativeInstruction);
        string segmentVariable = context.GetSegmentVariable(speculativeInstruction.Address.Segment);
        return EmittedCode.Line($"VerifySpeculativeEntryOrFail({segmentVariable}, 0x{speculativeInstruction.Address.Offset:X4}, {fieldName});");
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

    private EmittedCode BuildNodeBody(NodeEmissionPlan plan, MethodPlan method) {
        switch (plan.Node) {
            case CfgInstruction instruction:
                astEmitter.SetCurrentInstruction(instruction);
                EmittedCode body = astEmitter.LowerInstructionBody(instruction, instruction.ExecutionAst, plan.EmitsExternalEventCheckAfter);
                return cpuFaultWrapper.Wrap(instruction, body, method);
            case CfgSelectorNode selectorNode:
                // Uniform Accept dispatch: the selector's ExecutionAst is the AST SelectorNode marker, whose
                // Accept routes to VisitSelectorNode. A selector is always a block terminator, so it never has
                // a fallthrough to append (unlike an instruction body).
                return selectorNode.ExecutionAst.Accept(astEmitter);
            default:
                throw new NotSupportedException($"CFG C# generation does not support node {plan.Node.GetType().FullName} yet at {plan.Node.Address}.");
        }
    }

}
