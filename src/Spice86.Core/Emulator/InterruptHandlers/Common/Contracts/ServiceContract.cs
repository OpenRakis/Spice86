namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Contract of one interrupt service: which registers and flags it reads and writes,
/// and whether it touches emulated memory or I/O ports.
/// </summary>
/// <param name="Registers">What the service reads from its caller's registers and flags (including
/// the selector register) and writes on any path (flags whose value on the interrupt frame after
/// the service ran can differ from the caller's are what the stub's <c>iret</c> restores).</param>
/// <param name="Memory">Effect of the service on emulated memory.</param>
/// <param name="Io">Whether the service performs I/O port accesses.</param>
public sealed record ServiceContract(CallEffect Registers,
    ServiceMemoryEffect Memory, bool Io) {
    /// <summary>
    /// Contract used when the effects of a service cannot be described precisely:
    /// it may read and write every register and flag, memory and I/O ports.
    /// </summary>
    public static ServiceContract Unknown { get; } = new(
        new CallEffect(CpuValues.All, CpuValues.All), ServiceMemoryEffect.ReadWrite, true);
}
