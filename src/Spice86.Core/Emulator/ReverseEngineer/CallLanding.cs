namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// One mid-call landing a generated call accepts besides its return addresses: the address the callee's
/// dispatch was observed to land on while the call's frame was still open, and the generated entry that
/// must run there. Generated code passes the landings discovery observed for the call site; a landing on
/// any other registered override fails as untested.
/// </summary>
/// <param name="Address">The address the landing is on.</param>
/// <param name="Entry">The generated entry that runs when control lands on <see cref="Address"/>.</param>
public readonly record struct CallLanding(SegmentedAddress Address, Func<int, Action> Entry);
