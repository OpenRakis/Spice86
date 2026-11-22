namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Shared.Emulator.Memory;

/// <summary>
///     One hardware interrupt request that <see cref="DualPic" /> would have delivered to the CPU while a
///     delivery hold was active.
/// </summary>
/// <param name="Irq">IRQ line in the range 0 to 15. Lines 8 to 15 belong to the secondary controller.</param>
/// <param name="VectorNumber">Interrupt vector the request would have been delivered through.</param>
/// <param name="Cycles">Value of the CPU cycle counter when the CPU asked for the request.</param>
/// <param name="Address">CS:IP when the CPU asked for the request.</param>
public readonly record struct HeldInterruptDelivery(byte Irq, byte VectorNumber, long Cycles, SegmentedAddress Address);
