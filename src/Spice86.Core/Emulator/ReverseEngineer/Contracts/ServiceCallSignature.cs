namespace Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;


/// <summary>
/// Computes the typed C# parameter list an interrupt service helper gets from a
/// <see cref="ServiceContract"/>: one parameter per register field and per flag the
/// service reads, in <see cref="CpuValueParameters"/> order. The helper's parameters
/// are <c>ushort expectedReturnCs, ushort expectedReturnIp</c> followed by this list.
/// </summary>
public static class ServiceCallSignature {
    /// <summary>
    /// The parameter list of the helper for <paramref name="contract"/> on a vector
    /// whose selector register is <paramref name="selector"/>.
    /// </summary>
    /// <param name="contract">The service contract whose inputs become parameters.</param>
    /// <param name="selector">The selector register of the handler, or <see langword="null"/> for
    /// a selector-less handler.</param>
    /// <returns>The ordered parameter list.</returns>
    public static IReadOnlyList<TypedParameter> Parameters(ServiceContract contract,
        ServiceSelector? selector) =>
        CpuValueParameters.Decompose(contract.Registers.Reads & ~(selector?.ToCpuValues() ?? CpuValues.None));

    /// <summary>The output list of the helper for <paramref name="contract"/>: one value per
    /// register field and per flag the service writes, in <see cref="CpuValueParameters"/>
    /// order. A helper returns a scalar for one output, a named tuple for several and stays
    /// <see langword="void"/> for none.</summary>
    /// <param name="contract">The service contract whose writes become outputs.</param>
    /// <returns>The ordered output list.</returns>
    public static IReadOnlyList<TypedParameter> Outputs(ServiceContract contract) =>
        ReferenceEquals(contract, ServiceContract.Unknown)
            ? []
            : CpuValueParameters.Decompose(contract.Registers.Writes);
}
