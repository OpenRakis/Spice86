namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// What a partitioned program knows about one emulator-installed interrupt service vector: the
/// address the handler's entry stub was written to, the services it exposes, and the concrete
/// type of the installed handler (which the typed helper group registry resolves to a
/// <see cref="CSharpOverrideHelper"/> property when the vector has typed helpers).
/// </summary>
/// <param name="VectorNumber">The interrupt vector number.</param>
/// <param name="StubAddress">The address the handler's entry stub was installed at.</param>
/// <param name="Services">The services the handler exposes on the vector.</param>
/// <param name="HandlerType">The concrete type of the installed interrupt handler.</param>
internal sealed record InterruptServiceVector(byte VectorNumber, SegmentedAddress StubAddress,
    HandlerServices Services, Type HandlerType);
