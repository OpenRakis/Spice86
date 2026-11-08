namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using System.Reflection;

/// <summary>
/// One declared interrupt service: its selector value, its name, its contract, the kind of
/// string it reads at <c>DS:DX</c> when it takes one, the implementation generated code calls
/// directly when the vector is bound, and the register/flag binding of each of the
/// implementation's parameters in order.
/// </summary>
public sealed record InterruptServiceEntry(int SelectorValue, string Name,
    ServiceContract Contract, ServiceStringArgument StringArgument,
    MethodInfo? Implementation, IReadOnlyList<CpuValues> Inputs) {
    /// <summary>The entry of a service whose effects cannot be described precisely.</summary>
    public static InterruptServiceEntry Unknown { get; } = new(0, "Unknown",
        ServiceContract.Unknown, ServiceStringArgument.None, null, []);
}
