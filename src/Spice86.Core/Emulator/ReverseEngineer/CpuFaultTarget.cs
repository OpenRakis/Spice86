namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Describes a CPU fault handler target resolved at generation time and invoked at runtime.
/// </summary>
/// <param name="Handler">The segment and offset that the live IVT must match.</param>
/// <param name="Invoke">Calls the generated handler partition and returns its return action.</param>
public sealed record CpuFaultTarget(SegmentedAddress Handler, Func<Action> Invoke);