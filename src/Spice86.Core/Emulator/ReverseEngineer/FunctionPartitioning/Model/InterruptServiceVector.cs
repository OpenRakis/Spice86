namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// What a partitioned program knows about one emulator-installed interrupt service vector: the
/// address the handler's entry stub was written to, the services it exposes, and the name of the
/// <see cref="CSharpOverrideHelper"/> property holding the vector's typed helper group when it
/// has one.
/// </summary>
/// <param name="VectorNumber">The interrupt vector number.</param>
/// <param name="StubAddress">The address the handler's entry stub was installed at.</param>
/// <param name="Services">The services the handler exposes on the vector.</param>
/// <param name="GroupPropertyName">The typed helper group property name, or
/// <see langword="null"/> when the vector has no typed helpers.</param>
internal sealed record InterruptServiceVector(byte VectorNumber, SegmentedAddress StubAddress,
    HandlerServices Services, string? GroupPropertyName);
