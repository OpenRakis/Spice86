namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Shared.Emulator.Memory;

/// <summary>
///     A point where the CPU asked the interrupt controllers for a hardware interrupt.
/// </summary>
/// <remarks>
///     The interpreter asks after each instruction it runs alone and after each block it runs as a whole, when
///     the interrupt flag is set and no interrupt shadow (after <c>sti</c>, <c>mov ss</c>, <c>pop ss</c>) is
///     active. Generated code asks in <c>CSharpOverrideHelper.CheckExternalEvents</c> when the interrupt flag is
///     set: at each method entry, at each loop head on every pass, and after <c>sti</c>, <c>popf</c> and
///     <c>popfd</c>.
/// </remarks>
/// <param name="Cycles">Value of the CPU cycle counter when the CPU asked.</param>
/// <param name="Address">
///     CS:IP the CPU resumes at: the return address an interrupt delivered at this point pushes. In generated code
///     this is the address <c>CheckExternalEvents</c> was called with.
/// </param>
public readonly record struct InterruptDeliveryPoint(long Cycles, SegmentedAddress Address);
