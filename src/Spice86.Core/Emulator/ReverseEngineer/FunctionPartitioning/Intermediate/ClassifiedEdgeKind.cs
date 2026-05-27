namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

/// <summary>
/// Behavior-oriented classification of instruction-level CFG edges used during partitioning.
/// </summary>
internal enum ClassifiedEdgeKind {
    FallthroughOrInternal,
    Call,
    CallContinuation,
    MisalignedCallContinuation,
    RetTarget,
    CpuFault,
    Jump
}
