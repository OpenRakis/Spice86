namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Declares an interrupt service contract on the method implementing the service.
/// Use the parameterless form on a handler without a selector register: the
/// attribute declares the handler's single contract. Use the form with a selector
/// value on a handler whose services are chosen by a selector register. The
/// contract name is the method name.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InterruptServiceAttribute : Attribute {
    /// <summary>
    /// Declares the single contract of a handler without a selector register.
    /// </summary>
    public InterruptServiceAttribute() {
    }

    /// <summary>
    /// Declares a service chosen by the handler's selector register.
    /// </summary>
    /// <param name="selectorValue">The selector register value that invokes this service.</param>
    public InterruptServiceAttribute(int selectorValue) {
        SelectorValue = selectorValue;
    }

    /// <summary>The selector register value, or <see langword="null"/> for the single-contract form.</summary>
    public int? SelectorValue { get; }

    /// <summary>The contract name, or <see langword="null"/> to name the contract after the
    /// annotated method.</summary>
    public string? Name { get; init; }

    /// <summary>The register and flag set read by the service.</summary>
    public CpuValues Reads { get; init; }

    /// <summary>
    /// The register and flag set written by the service (whose value after the
    /// service can differ from the caller's).
    /// </summary>
    public CpuValues Writes { get; init; }

    /// <summary>The effect the service has on emulated memory.</summary>
    public ServiceMemoryEffect Memory { get; init; }

    /// <summary>Whether the service performs I/O port accesses.</summary>
    public bool Io { get; init; }

    /// <summary>The kind of string the service reads at <c>DS:DX</c>, when it takes one.</summary>
    public ServiceStringArgument StringArgument { get; init; }

    /// <summary>
    /// Whether the service's effects are not bounded. The contract is then the
    /// <see cref="ServiceContract.Unknown"/> singleton; combining it with the
    /// other properties is an error.
    /// </summary>
    public bool Unknown { get; init; }

    /// <summary>Builds the declared contract, named after the annotated method.</summary>
    /// <param name="methodName">The name of the annotated method.</param>
    internal ServiceContract ToContract(string methodName, string typeName) {
        if (Unknown) {
            if (Reads != CpuValues.None || Writes != CpuValues.None ||
                Memory != ServiceMemoryEffect.None || Io) {
                throw new InvalidOperationException(
                    $"{typeName}.{methodName}: [InterruptService(Unknown = true)] cannot be combined " +
                    "with Reads, Writes, Memory or Io");
            }
            if (StringArgument == ServiceStringArgument.None) {
                return ServiceContract.Unknown;
            }
            return new ServiceContract(Name ?? methodName, new CallEffect(CpuValues.All, CpuValues.All),
                ServiceMemoryEffect.ReadWrite, true) {
                IsUnknown = true,
                StringArgument = StringArgument
            };
        }
        return new ServiceContract(Name ?? methodName, new CallEffect(Reads, Writes), Memory, Io) {
            StringArgument = StringArgument
        };
    }
}
