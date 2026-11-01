namespace Spice86.Tests.InterruptHandlers;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers;

/// <summary>
/// One differential service contract test case: a service on an interrupt vector,
/// invoked with the given selector value and optional register/memory setup.
/// </summary>
public sealed record ServiceContractCase {
    /// <summary>The interrupt vector number.</summary>
    public required byte Vector { get; init; }

    /// <summary>
    /// The value written into the selector register (AH, AL or AX depending on the
    /// handler's services). Ignored for a single-service handler.
    /// </summary>
    public required int SelectorValue { get; init; }

    /// <summary>Whether the machine is built with XMS enabled.</summary>
    public bool NeedsXms { get; init; }

    /// <summary>Whether the machine is built with EMS enabled.</summary>
    public bool NeedsEms { get; init; }

    /// <summary>
    /// Optional extra setup applied after the generic input fill and before the fake
    /// interrupt frame is pushed. Use it to give read registers meaningful values or to
    /// write memory or host files the service consumes.
    /// </summary>
    public Action<ServiceContractMachine>? Setup { get; init; }

    /// <inheritdoc />
    public override string ToString() {
        return $"int{Vector:X2} selector=0x{SelectorValue:X}";
    }
}
