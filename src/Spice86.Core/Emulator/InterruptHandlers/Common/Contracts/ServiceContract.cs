namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Contract of one interrupt service: which registers and flags it reads and writes,
/// whether it touches emulated memory or I/O ports, and the name of the C# method
/// that implements it.
/// </summary>
/// <param name="Name">The C# method name of the service.</param>
/// <param name="Registers">What the service reads from its caller's registers and flags (including
/// the selector register) and writes on any path (flags whose value on the interrupt frame after
/// the service ran can differ from the caller's are what the stub's <c>iret</c> restores).</param>
/// <param name="Memory">Effect of the service on emulated memory.</param>
/// <param name="Io">Whether the service performs I/O port accesses.</param>
public sealed record ServiceContract(string Name, CallEffect Registers,
    ServiceMemoryEffect Memory, bool Io) {
    /// <summary>
    /// Contract used when the effects of a service cannot be described precisely:
    /// it may read and write every register and flag, memory and I/O ports.
    /// </summary>
    public static ServiceContract Unknown { get; } =
        new("Unknown", new CallEffect(CpuValues.All, CpuValues.All), ServiceMemoryEffect.ReadWrite, true)
            { IsUnknown = true };

    /// <summary>
    /// Whether this contract is the <see cref="Unknown"/> placeholder: true for it and for the
    /// unknown contracts that carry a <see cref="StringArgument"/>.
    /// </summary>
    public bool IsUnknown { get; init; }

    /// <summary>The kind of string the service reads at <c>DS:DX</c>, when it takes one.</summary>
    public ServiceStringArgument StringArgument { get; init; } = ServiceStringArgument.None;
}
