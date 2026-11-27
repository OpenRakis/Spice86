namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

/// <summary>
/// Behavior-oriented classification of instruction-level CFG edges used during partitioning.
/// </summary>
internal enum ClassifiedEdgeKind {
    FallthroughOrInternal,
    Call,
    CallContinuation,
    MisalignedCallContinuation,
    RedirectedCallContinuation,
    RetTarget,
    CpuFault,
    Jump,
    /// <summary>A <c>callback</c> successor other than the next in-memory instruction: the stub
    /// moved CS:IP to a fresh execution context (an EXEC child's entry, a terminate's resume
    /// landing), so the target roots its own partition instead of inlining into the stub's.
    /// </summary>
    CallbackMovedTarget
}
