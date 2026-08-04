namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
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
        writer.OpenBlock($"public virtual Action {method.MethodName}(int loadOffset)");
        if (method.NeedsEntryDispatchLabel) {
            writer.Label("entrydispatcher");
        }
        EmitEntryDispatch(writer, method);

        bool bodyCompletesNormally = true;
        foreach (NodeEmissionPlan nodePlan in method.NodeEmissionPlans) {
            if (nodePlan.IsBlockEntry) {
                writer.Label(nodePlan.Label);
                EmitBlockEntryExternalEventCheck(writer, nodePlan.Block);
            }
            // Speculative instructions are verified against memory immediately before they execute.
            // A per-instruction guard (rather than a single block-entry guard) is required so that
            // self-modifying code performed by an earlier instruction in the same block is detected
            // before the modified instruction's decode-time-baked body runs: at block entry the bytes
            // still match, the divergence only appears once the earlier store has executed.
            if (nodePlan.Node is CfgInstruction speculativeInstruction && speculativeInstruction.IsSpeculative) {
                EmitSpeculativeGuard(writer, speculativeInstruction);
            }
            EmittedCode nodeCode = BuildNode(writer, nodePlan.Node, method);
            EmittedCodeRenderer.Render(nodeCode, writer);
            bodyCompletesNormally = nodeCode.CompletesNormally;
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

    private void EmitEntryDispatch(CSharpSourceWriter writer, MethodPlan method) {
        if (method.NeedsEntryDispatch) {
            writer.OpenBlock("switch (loadOffset)");
            foreach (CfgCodePartitionEntry entry in method.Entries) {
                writer.Line($"case 0x{context.GetEntryLoadOffset(method.Partition, entry.Node):X4}:");
                writer.Line($"    goto {context.GetLabel(entry.Node)};");
            }
            writer.Line("default:");
            writer.Line("    throw FailAsUntested($\"Unknown generated entry loadOffset 0x{loadOffset:X4}\");");
            writer.CloseBlock();
            writer.Line();
            return;
        }

        // Single entry: only jump when the entry is not already the first node emitted in the body
        // (the entry point can be a reset vector at a higher address than the first emitted block).
        ICfgNode primaryEntry = method.PrimaryEntry.Node;
        if (method.NodeEmissionPlans.Count > 0 && method.NodeEmissionPlans[0].Node.Equals(primaryEntry)) {
            return;
        }
        writer.Line($"goto {context.GetLabel(primaryEntry)};");
        writer.Line();
    }

    private void EmitBlockEntryExternalEventCheck(CSharpSourceWriter writer, CfgBlock block) {
        // One external-event check per block, anchored to the block entry node's segmented address.
        // A block is the unit of straight-line execution between control-flow boundaries, so a single
        // check at block entry is sufficient: once entered, execution runs to the terminator without an
        // intervening external-event boundary. Anchoring to the block entry keeps the expected resume
        // point aligned with the label other transfers goto, so a handler returning into the middle of a
        // block is still rejected.
        ICfgNode entry = block.Entry;
        writer.Line($"CheckExternalEvents({context.GetSegmentVariable(entry.Address.Segment)}, 0x{entry.Address.Offset:X4});");
    }

    private EmittedCode BuildNode(CSharpSourceWriter writer, ICfgNode node, MethodPlan method) {
        switch (node) {
            case CfgInstruction instruction:
                string assembly = instruction.DisplayAst.Accept(assemblyRenderer);
                writer.Line($"// {instruction.Address} {assembly}");
                astEmitter.SetCurrentInstructionAddress(instruction.Address);
                EmittedCode body = astEmitter.LowerInstructionBody(instruction, instruction.ExecutionAst);
                return cpuFaultWrapper.Wrap(instruction, body, method);
            case CfgSelectorNode selectorNode:
                writer.Line($"// {selectorNode.Address} selector");
                // Uniform Accept dispatch: the selector's ExecutionAst is the AST SelectorNode marker, whose
                // Accept routes to VisitSelectorNode. A selector is always a block terminator, so it never has
                // a fallthrough to append (unlike an instruction body).
                return selectorNode.ExecutionAst.Accept(astEmitter);
            default:
                throw new NotSupportedException($"CFG C# generation does not support node {node.GetType().FullName} yet at {node.Address}.");
        }
    }

    /// <summary>
    /// Emits a <c>VerifySpeculativeEntryOrFail</c> guard for a single speculative instruction. The guard
    /// re-reads the instruction's bytes from memory immediately before its body executes and fails as
    /// untested if they no longer match the signature decoded at exploration time. Emitting one guard per
    /// speculative instruction (rather than a single block-entry guard covering the whole run) is what lets
    /// the generated code detect self-modifying code that an earlier instruction in the same block performs
    /// against a later speculative instruction: an entry-only guard runs before any instruction executes and
    /// so cannot observe such a mutation.
    /// </summary>
    private void EmitSpeculativeGuard(CSharpSourceWriter writer, CfgInstruction speculativeInstruction) {
        IReadOnlyList<byte?> signatureValue = speculativeInstruction.Signature.SignatureValue;
        if (signatureValue.Count == 0) {
            return;
        }
        string signatureBytes = string.Join(", ", signatureValue.Select(value => value is byte byteValue ? $"(byte)0x{byteValue:X2}" : "null"));
        string segmentVariable = context.GetSegmentVariable(speculativeInstruction.Address.Segment);
        writer.Line($"VerifySpeculativeEntryOrFail({segmentVariable}, 0x{speculativeInstruction.Address.Offset:X4}, [{signatureBytes}]);");
    }
}
