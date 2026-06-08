namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

using SelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>
/// Converts the analyzed CFG into the ordered list of "what to write": which segment fields to declare,
/// which overrides to register, and which methods to emit (with their node order and labels). The plan is
/// pure data — emitters read it top to bottom without re-analyzing the graph.
/// </summary>
internal static class GenerationPlanBuilder {
    public static GenerationPlan Build(CfgGeneratorContext context) {
        // A cyclic-flow transfer's back-edge targets the entrydispatcher label, so both partitions it
        // connects must emit that label (see MethodPlan.NeedsEntryDispatchLabel).
        HashSet<CfgCodePartition> cyclicFlowPartitions = context.Program.Transfers
            .Where(transfer => transfer.Kind == CfgCodePartitionTransferKind.CyclicCrossPartitionFlow)
            .SelectMany(transfer => new[] { transfer.FromPartition, transfer.ToPartition })
            .ToHashSet();
        List<MethodPlan> methods = context.Program.Partitions
            .OrderBy(partition => partition.Id)
            .Select(partition => BuildMethodPlan(context, partition, cyclicFlowPartitions.Contains(partition)))
            .ToList();
        return new GenerationPlan {
            SegmentFields = BuildSegmentFields(context),
            OverrideRegistrations = BuildOverrideRegistrations(context, methods),
            Methods = methods
        };
    }

    private static List<SegmentFieldPlan> BuildSegmentFields(CfgGeneratorContext context) {
        List<SegmentFieldPlan> fields = context.SegmentVariables
            .Select(entry => new SegmentFieldPlan(entry.Key, entry.Value))
            .OrderBy(field => field.Segment)
            .ToList();
        // GeneratorAnalysis guarantees one variable per distinct segment; fail if two distinct segments ever
        // collide on the same generated field name.
        Dictionary<string, ushort> segmentByFieldName = new();
        foreach (SegmentFieldPlan field in fields) {
            if (segmentByFieldName.TryGetValue(field.FieldName, out ushort existingSegment) && existingSegment != field.Segment) {
                throw new NotSupportedException(
                    $"Generated segment variable {field.FieldName} maps to conflicting observed segments 0x{existingSegment:X4} and 0x{field.Segment:X4}.");
            }
            segmentByFieldName[field.FieldName] = field.Segment;
        }
        return fields;
    }

    private static List<OverrideRegistration> BuildOverrideRegistrations(CfgGeneratorContext context, IReadOnlyList<MethodPlan> methods) {
        List<OverrideRegistration> registrations = [];
        foreach (MethodPlan method in methods) {
            foreach (CfgCodePartitionEntry entry in method.Entries) {
                int loadOffset = context.GetEntryLoadOffset(method.Partition, entry.Node);
                registrations.Add(new OverrideRegistration(
                    context.GetSegmentVariable(entry.Address.Segment),
                    entry.Address.Offset,
                    method.MethodName,
                    loadOffset,
                    context.GetPartitionBaseName(method.Partition)));
            }
        }
        return DeduplicateByAddress(methods, registrations);
    }

    /// <summary>
    /// Collapses override registrations that resolve to the same <c>DefineFunction</c> address. The override
    /// catalogue is keyed by segmented address, so only one override can be installed per address; emitting a
    /// second <c>DefineFunction</c> at the same address throws at runtime.
    /// <para>
    /// Self-modifying code is the source of these collisions: a selector node and its instruction variants
    /// live at one address but become distinct partitions, and several can carry a primary (loadOffset 0)
    /// entry at that address. The surviving registration is the one whose partition entry is the selector
    /// node, because when emulated code calls that address the selector inspects the current memory signature
    /// and dispatches to the correct variant (failing as untested on an unobserved signature). The variant
    /// partitions remain reachable through the generated call/jump method references, so they do not need
    /// their own address registration. When no selector entry exists, the lowest partition id wins so output
    /// stays deterministic.
    /// </para>
    /// </summary>
    private static List<OverrideRegistration> DeduplicateByAddress(IReadOnlyList<MethodPlan> methods, List<OverrideRegistration> registrations) {
        Dictionary<string, int> partitionIdByMethodName = methods.ToDictionary(
            method => method.MethodName,
            method => method.Partition.Id);
        HashSet<string> selectorMethodNames = methods
            .Where(method => method.PrimaryEntry.Node is SelectorNode)
            .Select(method => method.MethodName)
            .ToHashSet();

        return registrations
            .GroupBy(registration => (registration.SegmentVariable, registration.Offset))
            .Select(group => group
                .OrderByDescending(registration => selectorMethodNames.Contains(registration.MethodName))
                .ThenBy(registration => partitionIdByMethodName[registration.MethodName])
                .First())
            .ToList();
    }

    private static MethodPlan BuildMethodPlan(CfgGeneratorContext context, CfgCodePartition partition, bool isCyclicFlowParticipant) {
        List<CfgBlock> blocks = partition.Blocks
            .OrderBy(block => block.Entry.Address.Linear)
            .ThenBy(block => block.Id)
            .ToList();
        List<ICfgNode> nodes = blocks.SelectMany(block => block.Instructions).ToList();
        List<NodeEmissionPlan> nodeEmissionPlans = [];
        foreach (CfgBlock block in blocks) {
            // First instruction of each block is the entry: it gets the label and event check.
            bool isBlockEntry = true;
            foreach (ICfgNode node in block.Instructions) {
                nodeEmissionPlans.Add(new NodeEmissionPlan(node, block, context.GetLabel(node), isBlockEntry));
                isBlockEntry = false;
            }
        }
        // Successor in emission order; last node has no fallthrough target.
        Dictionary<ICfgNode, ICfgNode?> nextNodeByNode = new();
        for (int i = 0; i < nodes.Count; i++) {
            nextNodeByNode[nodes[i]] = i + 1 < nodes.Count ? nodes[i + 1] : null;
        }

        return new MethodPlan(partition, context.GetMethodName(partition), context.GetEntries(partition), blocks,
            nodes, nodeEmissionPlans, nextNodeByNode, isCyclicFlowParticipant);
    }
}
