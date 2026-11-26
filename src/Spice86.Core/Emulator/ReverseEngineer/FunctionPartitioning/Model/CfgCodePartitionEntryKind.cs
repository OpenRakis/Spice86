namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// Entry evidence classification.
/// </summary>
internal enum CfgCodePartitionEntryKind {
    ExecutionContextEntry,
    FunctionEntry,
    GraphComponentEntry,
    ReturnTargetEntry,
    SharedEntry,
    /// <summary>An EXEC child program entry the DOS dispatch callback moved CS:IP to: its
    /// returns are the child's terminates, and their demand is the live-in of the parent's
    /// ExecChild continuations - not the whole machine an execution context entry implies.</summary>
    ExecChildEntry
}