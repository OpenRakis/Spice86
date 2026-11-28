namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using System.Reflection;

/// <summary>
/// One declared interrupt service: its selector value, its name, its contract, the kind of
/// string it reads at <c>DS:DX</c> when it takes one, the implementation generated code calls
/// directly when the vector is bound, and the register/flag binding of each of the
/// implementation's parameters in order.
/// </summary>
/// <param name="Al">The AL sub-selector value the entry is a variant of
/// <see cref="SelectorValue"/>, or <see cref="InterruptServiceAttribute.NoAl"/> for the base
/// entry of the selector value.</param>
public sealed record InterruptServiceEntry(int SelectorValue, string Name,
    ServiceContract Contract, ServiceStringArgument StringArgument,
    MethodInfo? Implementation, IReadOnlyList<CpuValues> Inputs,
    int Al = InterruptServiceAttribute.NoAl) {
    /// <summary>The entry of a service whose effects cannot be described precisely.</summary>
    public static InterruptServiceEntry Unknown { get; } = new(0, "Unknown",
        ServiceContract.Unknown, ServiceStringArgument.None, null, []);

    /// <summary>The AL sub-selector variants of this entry: entries sharing its
    /// <see cref="SelectorValue"/> that apply only when AL is a known constant. Runtime
    /// dispatch stays on the selector register alone; only the generator resolves a
    /// variant.</summary>
    public IReadOnlyList<InterruptServiceEntry> Variants { get; init; } = [];

    /// <summary>Whether the service implementation reads the interrupt frame pushed by the
    /// <c>int</c> (for example the caller CS:IP an EXEC service stores in the child PSP);
    /// a generated typed call then keeps the frame-pushing
    /// <c>ServiceCall(expectedReturnCs, expectedReturnIp, ...)</c> form.</summary>
    public bool ReadsFrame { get; init; }
}
