namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

using System.Linq;

/// <summary>
/// Builds instruction-level partition edge records from included CFG blocks.
/// </summary>
internal sealed class CfgPartitionEdgeCollector {
    public List<CfgPartitionEdgeRecord> Collect(Dictionary<int, CfgBlock> blocksById) {
        List<CfgPartitionEdgeRecord> edgeRecords = new();
        HashSet<EdgeIdentity> seen = new();
        foreach (CfgBlock block in CfgPartitionOrdering.BlocksByAddressAndId(blocksById.Values)) {
            foreach (ICfgNode node in block.Instructions) {
                if (node is CfgInstruction instruction) {
                    AddInstructionEdges(blocksById, edgeRecords, seen, block, instruction);
                } else {
                    AddUntypedNodeEdges(blocksById, edgeRecords, seen, block, node);
                }
            }
        }
        return edgeRecords;
    }

    private static void AddInstructionEdges(
        Dictionary<int, CfgBlock> blocksById,
        List<CfgPartitionEdgeRecord> edgeRecords,
        HashSet<EdgeIdentity> seen,
        CfgBlock sourceBlock,
        CfgInstruction instruction) {
        foreach (KeyValuePair<InstructionSuccessorType, ISet<ICfgNode>> entry in instruction.SuccessorsPerType) {
            foreach (ICfgNode target in entry.Value) {
                CfgBlock? targetBlock = target.ContainingBlock;
                if (targetBlock == null || !blocksById.ContainsKey(targetBlock.Id)) {
                    continue;
                }
                ClassifiedEdgeKind kind = ClassifyInstructionEdge(instruction, entry.Key);
                AddEdgeRecord(edgeRecords, seen, sourceBlock, targetBlock, instruction, target, entry.Key, kind);
            }
        }
    }

    private static void AddUntypedNodeEdges(
        Dictionary<int, CfgBlock> blocksById,
        List<CfgPartitionEdgeRecord> edgeRecords,
        HashSet<EdgeIdentity> seen,
        CfgBlock sourceBlock,
        ICfgNode node) {
        foreach (ICfgNode target in node.Successors) {
            CfgBlock? targetBlock = target.ContainingBlock;
            if (targetBlock == null || !blocksById.ContainsKey(targetBlock.Id)) {
                continue;
            }
            AddEdgeRecord(edgeRecords, seen, sourceBlock, targetBlock, node, target, InstructionSuccessorType.Normal,
                ClassifiedEdgeKind.FallthroughOrInternal);
        }
    }

    private static ClassifiedEdgeKind ClassifyInstructionEdge(CfgInstruction instruction, InstructionSuccessorType successorType) =>
        (instruction.IsCall, instruction.IsReturn, instruction.IsJump, successorType) switch {
            (_, _, _, InstructionSuccessorType.CpuFault) => ClassifiedEdgeKind.CpuFault,
            (true, _, _, InstructionSuccessorType.Normal) => ClassifiedEdgeKind.Call,
            (true, _, _, InstructionSuccessorType.CallToReturn) => ClassifiedEdgeKind.CallContinuation,
            (true, _, _, InstructionSuccessorType.CallToMisalignedReturn) => ClassifiedEdgeKind.MisalignedCallContinuation,
            (_, true, _, InstructionSuccessorType.Normal) => ClassifiedEdgeKind.RetTarget,
            (_, _, true, InstructionSuccessorType.Normal) => ClassifiedEdgeKind.Jump,
            _ => ClassifiedEdgeKind.FallthroughOrInternal,
        };

    private static void AddEdgeRecord(
        List<CfgPartitionEdgeRecord> edgeRecords,
        HashSet<EdgeIdentity> seen,
        CfgBlock sourceBlock,
        CfgBlock targetBlock,
        ICfgNode sourceNode,
        ICfgNode targetNode,
        InstructionSuccessorType successorType,
        ClassifiedEdgeKind kind) {
        EdgeIdentity identity = new(sourceBlock.Id, targetBlock.Id, sourceNode.Id, targetNode.Id, successorType, kind);
        if (!seen.Add(identity)) {
            return;
        }
        edgeRecords.Add(new CfgPartitionEdgeRecord(sourceBlock, targetBlock, sourceNode, targetNode, successorType, kind));
    }

    private readonly record struct EdgeIdentity(
        int SourceBlockId,
        int TargetBlockId,
        int SourceNodeId,
        int TargetNodeId,
        InstructionSuccessorType SuccessorType,
        ClassifiedEdgeKind Kind);
}
