namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// Partition origin classification.
/// </summary>
internal enum CfgCodePartitionKind {
    /// <summary>
    /// Partition rooted in observed execution evidence.
    /// </summary>
    Observed,
    /// <summary>
    /// Partition extracted from code shared by more than one observed partition.
    /// </summary>
    Synthetic
}