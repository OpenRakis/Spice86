namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

internal readonly record struct ResolvedCfgEdge(
    ICfgNode Source,
    ICfgNode Target,
    InstructionSuccessorType SuccessorType,
    CfgCodePartitionTransferKind? PartitionTransferKind);