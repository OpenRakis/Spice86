namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// What callable code reads from its caller's registers and flags (<see cref="Reads"/>) and may
/// leave changed when it hands control back (<see cref="Writes"/>).
/// </summary>
/// <param name="Reads">Registers and caller flags the code reads, including a selector register.</param>
/// <param name="Writes">Registers the code writes on any path, and flags whose value when control
/// returns can differ from the caller's.</param>
public sealed record CallEffect(CpuValues Reads, CpuValues Writes);
