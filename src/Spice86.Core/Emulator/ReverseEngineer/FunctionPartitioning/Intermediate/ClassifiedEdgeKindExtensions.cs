namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

/// <summary>
/// Extension methods for <see cref="ClassifiedEdgeKind"/>.
/// </summary>
internal static class ClassifiedEdgeKindExtensions {
    /// <summary>
    /// Returns true when the edge stays within the same function activation: fallthrough, jump,
    /// or aligned call-continuation.
    /// Misaligned call-continuations are excluded: they indicate the callee returned to an address
    /// other than the CALL's next instruction, so the target belongs to its own partition and is
    /// reached via a dynamic-return dispatcher rather than merged into the caller's region.
    /// </summary>
    public static bool IsOwnershipPreserving(this ClassifiedEdgeKind kind) =>
        kind == ClassifiedEdgeKind.FallthroughOrInternal
        || kind == ClassifiedEdgeKind.Jump
        || kind == ClassifiedEdgeKind.CallContinuation;
}
