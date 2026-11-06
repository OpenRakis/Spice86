namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using System.Reflection;

/// <summary>
/// The services declared by <see cref="InterruptServiceAttribute"/> on a type:
/// at most one single service (for handlers without a selector register) and the
/// selector-chosen services, each paired with the method implementing it.
/// </summary>
public sealed record InterruptServiceDeclarations(
    (MethodInfo Method, InterruptServiceEntry Entry)? SingleService,
    IReadOnlyList<(MethodInfo Method, InterruptServiceEntry Entry)> Services);
