namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// Inter-partition transfer classification.
/// </summary>
internal enum CfgCodePartitionTransferKind {
    CallOut,
    CpuFault,
    AlignedReturn,
    DynamicReturn,
    CrossPartitionFlow,
    CyclicCrossPartitionFlow
}