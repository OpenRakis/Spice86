namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

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
}
