namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// One DOS or BIOS service call, as reported to an <see cref="IServiceCallObserver"/>.
/// </summary>
/// <remarks>
/// A call of one service with the same inputs gives equal <see cref="Vector"/>, <see cref="Selector"/>,
/// <see cref="Entry"/> and <see cref="Inputs"/> whichever way it was reached. Only <see cref="Direct"/> tells
/// the forms apart.
/// </remarks>
/// <param name="Vector">Interrupt vector of the service.</param>
/// <param name="Selector">Value of the handler's selector register (AH, AL or AX, see
/// <see cref="ServiceSelector"/>), or <see langword="null"/> for a handler with a single service.</param>
/// <param name="Entry">Declared entry of the service, with its name and contract.
/// <see cref="InterruptServiceEntry.Unknown"/> when the handler declares none for the selector value.</param>
/// <param name="Direct">Whether generated code called the implementing method directly. Otherwise the call
/// came through the handler's stub, from emulated code or from <c>InterruptCall</c>.</param>
/// <param name="Inputs">Value of each input the entry binds to its implementation, in the order of
/// <see cref="InterruptServiceEntry.Inputs"/>. Empty when the entry binds no implementation: such a service is
/// always reached through the stub, where the registers of the contract's reads hold the inputs and the
/// observer can read them from the CPU state.</param>
public readonly record struct ServiceCall(byte Vector, int? Selector, InterruptServiceEntry Entry, bool Direct,
    IReadOnlyList<ServiceCallInput> Inputs);

/// <summary>
/// One input of a service call: the register or flag the service entry binds, and its value.
/// </summary>
/// <param name="Binding">The register, flag or DS:DX address, as in <see cref="InterruptServiceEntry.Inputs"/>.
/// </param>
/// <param name="Value">The value, encoded as <see cref="ServiceCallInputs.Read"/> describes.</param>
public readonly record struct ServiceCallInput(CpuValues Binding, uint Value);
