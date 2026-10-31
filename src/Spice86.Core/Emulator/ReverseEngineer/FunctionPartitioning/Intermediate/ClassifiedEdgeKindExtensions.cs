namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

/// <summary>
/// Extension methods for <see cref="ClassifiedEdgeKind"/>.
/// </summary>
internal static class ClassifiedEdgeKindExtensions {
    /// <summary>
    /// Returns true when the edge stays within the same function activation: fallthrough, jump,
    /// aligned call-continuation, or redirected call-continuation.
    /// Redirected continuations consumed the call's own frame, so the target belongs to the caller's
    /// activation even though it is not the CALL's next instruction. Misaligned call-continuations
    /// are excluded: their return did not match the call's frame by stack, so the target belongs to
    /// its own partition and is reached via a dynamic-return dispatcher.
    /// </summary>
    public static bool IsOwnershipPreserving(this ClassifiedEdgeKind kind) =>
        kind == ClassifiedEdgeKind.FallthroughOrInternal
        || kind == ClassifiedEdgeKind.Jump
        || kind == ClassifiedEdgeKind.CallContinuation
        || kind == ClassifiedEdgeKind.RedirectedCallContinuation;
}
