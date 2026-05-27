namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// Instruction-level CFG edge enriched with partitioning classification.
/// </summary>
internal sealed record CfgPartitionEdgeRecord(
    CfgBlock SourceBlock,
    CfgBlock TargetBlock,
    ICfgNode SourceNode,
    ICfgNode TargetNode,
    InstructionSuccessorType SuccessorType,
    ClassifiedEdgeKind Kind);
