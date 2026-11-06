namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// One declared interrupt service: its selector value, its name, its contract, the kind of
/// string it reads at <c>DS:DX</c> when it takes one, and how generated helpers execute it.
/// </summary>
public sealed record InterruptServiceEntry(int SelectorValue, string Name,
    ServiceContract Contract, ServiceStringArgument StringArgument,
    ServiceExecutionPath ExecutionPath) {
    /// <summary>The entry of a service whose effects cannot be described precisely.</summary>
    public static InterruptServiceEntry Unknown { get; } = new(0, "Unknown",
        ServiceContract.Unknown, ServiceStringArgument.None, ServiceExecutionPath.Stub);
}
