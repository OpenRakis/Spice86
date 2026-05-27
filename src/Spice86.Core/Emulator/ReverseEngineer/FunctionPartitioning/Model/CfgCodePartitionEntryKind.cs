namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// Entry evidence classification.
/// </summary>
internal enum CfgCodePartitionEntryKind {
    ExecutionContextEntry,
    FunctionEntry,
    GraphComponentEntry,
    ReturnTargetEntry,
    SharedEntry
}